using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthClient;

public class Program
{
    private static readonly HttpClient client = new();
    private static TokenResponse? currentToken;
    private const string ClientId = "client_ID_goes_here";
    private const string ClientSecret = "client_secret_goes_here";
    private const string RedirectUri = "http://localhost:5000/callback";
    private const string AuthEndpoint = "https://accounts.spotify.com/authorize";
    private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string ApiEndpoint = "https://api.spotify.com/v1";

    // Song URI.
    private const string TargetSongUri = "spotify:track:4z0PnuB07fxtVZZRWsCfxb";

    public static async Task Main()
    {
        // Start local server to receive the callback
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri.TrimEnd('/') + "/");
        listener.Start();

        // Generate the authorization URL and open it in the default browser
        var state = Guid.NewGuid().ToString();
        var scope = "user-read-private user-modify-playback-state user-read-playback-state";
        var authUrl = $"{AuthEndpoint}?" +
            $"response_type=code&" +
            $"client_id={ClientId}&" +
            $"scope={scope}&" +
            $"redirect_uri={RedirectUri}&" +
            $"state={state}";

        OpenBrowser(authUrl);
        Console.WriteLine("Browser opened for authentication...");

        // Wait for the callback
        var context = await listener.GetContextAsync();
        var response = context.Response;

        // Get the authorization code from the callback
        var queryParams = System.Web.HttpUtility.ParseQueryString(context.Request.Url!.Query);
        var code = queryParams["code"];
        var returnedState = queryParams["state"];

        if (string.IsNullOrEmpty(code) || returnedState != state)
        {
            await SendResponse(response, "Authentication failed!");
            return;
        }

        // Exchange the code for tokens
        currentToken = await GetTokensFromCode(code);
        if (currentToken == null)
        {
            await SendResponse(response, "Failed to get access token!");
            return;
        }

        await SendResponse(response, "Authentication successful! You can close this window.");

        // Get and display the username
        var username = await GetUsername();
        Console.WriteLine($"\nConnected as: {username}");

        // Check playback state and force play if appropriate
        await ManagePlayback();

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task ManagePlayback()
    {
        try
        {
            // First check if user is currently playing something
            var playbackState = await GetPlaybackState();
            if (playbackState == null)
            {
                Console.WriteLine("No active playback session found.");
                return;
            }

            // Force Spotify to play the specified song
            var success = await ForcePlaySong();
            if (success)
            {
                Console.WriteLine("Changed playback.");
            }
            else
            {
                Console.WriteLine("Failed to change playback.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error managing playback: {ex.Message}");
        }
    }

    private static async Task<PlaybackState?> GetPlaybackState()
    {
        if (currentToken == null) return null;

        // Check if token needs refresh
        if (currentToken.ExpiresAt <= DateTime.UtcNow)
        {
            currentToken = await RefreshAccessToken(currentToken.RefreshToken);
            if (currentToken == null) return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/me/player");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken.AccessToken);

        var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null; // No active playback session
        }

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<PlaybackState>(json);
        }

        return null;
    }

    private static async Task<bool> ForcePlaySong()
    {
        if (currentToken == null) return false;

        // Check if token needs refresh
        if (currentToken.ExpiresAt <= DateTime.UtcNow)
        {
            currentToken = await RefreshAccessToken(currentToken.RefreshToken);
            if (currentToken == null) return false;
        }

        var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiEndpoint}/me/player/play");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken.AccessToken);

        var playbackRequest = new
        {
            uris = new[] { TargetSongUri }
        };

        var json = JsonSerializer.Serialize(playbackRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }


    private static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Console.WriteLine($"Please open this URL in your browser:\n{url}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open browser automatically. Please open this URL manually:\n{url}");
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task<TokenResponse?> GetTokensFromCode(string code)
    {
        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", RedirectUri },
            { "client_id", ClientId },
            { "client_secret", ClientSecret }
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await client.PostAsync(TokenEndpoint, content);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TokenResponse>(json);
    }

    private static async Task<TokenResponse?> RefreshAccessToken(string refreshToken)
    {
        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken },
            { "client_id", ClientId },
            { "client_secret", ClientSecret }
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await client.PostAsync(TokenEndpoint, content);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TokenResponse>(json);
    }

    private static async Task<string> GetUsername()
    {
        while (true)
        {
            if (currentToken == null) return "Not authenticated";

            // Check if token needs refresh
            if (currentToken.ExpiresAt <= DateTime.UtcNow)
            {
                currentToken = await RefreshAccessToken(currentToken.RefreshToken);
                if (currentToken == null) return "Token refresh failed";
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken.AccessToken);

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<UserInfo>(json);
                return userInfo?.DisplayName ?? "Unknown";
            }

            if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                return "Failed to get username";

            // If unauthorized, try refreshing token once
            currentToken = await RefreshAccessToken(currentToken.RefreshToken);
            if (currentToken == null) return "Token refresh failed";
        }
    }

    private static async Task SendResponse(HttpListenerResponse response, string message)
    {
        var buffer = System.Text.Encoding.UTF8.GetBytes($"<html><body>{message}</body></html>");
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonIgnore]
    public DateTime ExpiresAt => DateTime.UtcNow.AddSeconds(ExpiresIn);
}

public class UserInfo
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";
}

public class PlaybackState
{
    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("device")]
    public Device? Device { get; set; }
}

public class Device
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
