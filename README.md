## Intro
This is a simple demonstration of authenticating with the Spotify API and interacting with endpoints.

As delivered, it will check the active user queue - if we see something playing, stop playback, kill the queue, and replace it with the song configured.

## Setup/Usage
Configure an application via your developer dashboard:

```
https://developer.spotify.com/dashboard/
```

Configure the following:

```
Redirect URI: http://localhost:5000/callback

APIs used: Web API (basic endpoint interactions)
```

And in utility, change the following:

```
ClientId, ClientSecret, TargetSongUri
```

# Notes
I've parred this utility down significantly, and it should run as expected with the basic VS build/run. If not, let me know.

Token refreshing has been removed, since it was complicated. If you need it, let me know as well.
