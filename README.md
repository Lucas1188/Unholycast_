# UnholyCast Sonos Bridge 🎧

Turn a Sonos speaker into a **YouTube / HTTP audio cast receiver**.

This service works **together with yt-cast-receiver-http** and acts as the Sonos playback bridge + streaming server. It transcodes incoming streams with FFmpeg, hosts them as MP3, and commands Sonos to play them using Python SoCo via Python.NET.

---

## 🧠 What this project does

```
YouTube sender / browser / yt-cast-receiver-http
                ↓
        UnholyCast Sonos Bridge (this service)
                ↓
          Sonos speaker group
```

• Receives playback commands
• Transcodes audio with FFmpeg
• Streams MP3 to Sonos
• Controls Sonos transport + volume
• Persists playback metadata

Think of it as a **Sonos-compatible Chromecast Audio receiver**.

---

## ✨ Features

### Sonos control

* Play / pause / resume
* Seek support
* Volume + mute
* Transport polling
* Targets a Sonos group leader

### Streaming engine

* On-demand FFmpeg transcoding
* Streams MP3 with HTTP byte-range support
* Streams while file is still being written
* Handles Sonos buffering quirks

### Persistence

* Resume partially downloaded streams

### Python integration

Runs the SoCo library inside .NET using Python.NET.

---

## 🧩 Companion project

This bridge is designed to run alongside **[yt-cast-receiver-http](https://github.com/lucas1188/yt-cast-receiver-http)**.

That service:

* Receives cast commands from sender apps
* Extracts stream URLs
* Calls this service’s `/start` endpoint

This service:

* Transcodes + hosts the audio
* Commands Sonos to play it

---

## 🏗 Architecture overview

```
ASP.NET Minimal API
        │
        ├── Sonos Manager (Python SoCo via Python.NET)
        ├── FFmpeg Service (transcoding pipeline)
        ├── Playback Store (SQLite)
        └── MP3 HTTP Streamer (range + live file growth)
```
---

## Docker Compose (Recommended)
```yaml
  unholycast:
      image: ghcr.io/lucas1188/unholycast:dotnet
      container_name: cast-sonos
      network_mode: host
      command:
        - --leader
        - WorkroomLeader
        - --port
        - "6767"
      restart: unless-stopped

  yt-cast-receiver-http:
      image: ghcr.io/lucas1188/yt-cast-receiver-http:dotnet
      container_name: cast-receiver
      network_mode: host
      command:
        - --
        - --name
        - 'UnholycastAll'
        - --port
        - "6767"
        - --no-ui
      depends_on:
        - unholycast
      restart: unless-stopped
```

## 🐳 Docker Standalone

A prebuilt container is available:
```
ghcr.io/lucas1188/unholycast:dotnet
```

### Run container

```bash
docker run -d \
  --name unholycast \
  --network host \
  -v ./data:/app/files \
  -v ./db:/app \
  ghcr.io/lucas1188/unholycast:dotnet \
  --port 9999 --leader "Living Room"
```

## ⚙️ Configuration

Runtime arguments:

| Argument   | Description                    |
| ---------- | ------------------------------ |
| `--port`   | HTTP API + stream server port  |
| `--leader` | Sonos leader device name or IP |

Example:

```bash
--port 9999 --leader "Living Room"
```

---

## 📁 Persistent data

Mount volumes to persist state:

| Path in container | Purpose                    |
| ----------------- | -------------------------- |
| `/app/files`      | Live transcoded MP3 files  |
| `/app/db.db`      | Playback metadata database |

---

## 🔌 HTTP API

### Playback status

`GET /poll`

```json
{
  "status": {
    "current_transport_status": "...",
    "current_transport_state": "...",
    "current_speed": "1"
  }
}
```

---

### Start streaming (main entrypoint)

`GET /start`

Parameters:

| Param       | Description             |
| ----------- | ----------------------- |
| `streamUrl` | Direct audio stream URL |
| `videoId`   | Unique media ID         |
| `title`     | Title                   |
| `duration`  | Seconds                 |
| `position`  | Start position          |
| `src`       | Source (yt etc)         |
| `channel`   | Channel name            |
| `artist`    | Artist                  |
| `album`     | Album                   |

Example:

```
/start?streamUrl=<url>&videoId=abc123&title=Song&duration=300
```

Flow:

1. Stop Sonos if playing
2. Start FFmpeg transcoding
3. Start HTTP MP3 streaming
4. Command Sonos to play the stream
5. Seek if requested

---

### Pause / Resume

```
GET /pause
GET /resume
```

---

### Seek

```
GET /seek?pos=120
```

---

### Position & duration

```
GET /position
GET /duration
```

---

### Volume

Get:

```
GET /volume
```

Set:

```
POST /volume
{
  "level": 20,
  "muted": false
}
```

---

### Playback store

```
GET /store
```

Returns stored media metadata.

---

## 🎵 Streaming endpoint

Sonos device is expected to actually play:

```
GET /<videoId>.mp3
```

## ❤️ Why this exists

Sonos doesn’t support YouTube casting.
So I made it anyway.
