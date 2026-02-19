using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Unholycast_
{
    public static class ShutdownToken
    {
        public static CancellationToken Token;
    }
    public class UnholySonosManager : UnholyStateManager
    {
        private SocoRuntime _runtime;
        private dynamic _device;
        private CancellationTokenSource _cancellationTokenSource;
        private int _timeoutDuration = 5;
        public SonosTransportState cTransportState {  get; private set; }
        public Volume cVolume { get; private set; }
        public SonosTrackInfo cTrackInfo { get; private set; }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public UnholySonosManager(SocoRuntime runtime, dynamic device, SonosPoller poller) : base(poller)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        {
            _device = device;
            _runtime = runtime;
            _cancellationTokenSource = new CancellationTokenSource();
            poller.OnPolledState += (s, p, v) =>
            {
                cTransportState = s;
                cTrackInfo = p;
                cVolume = v;
            }; 
        }
        public record class CurrentPlaying
        {
            public required PlaybackInfo _playbackInfo;
            public required string Didl;
            public required string url;
        }
        public CurrentPlaying? _currentPlaying { get; private set; }
        public async Task<bool> TrySeek(int time)
        {
            if (_currentPlaying == null) return false;
            var ct = new CancellationTokenSource();
            var timeout = DateTime.Now+TimeSpan.FromSeconds(10);

            _poller.PausePoll(10, ct.Token);
            var cS = _poller.PollNow();
            if (cS == State.PLAYING || cS == State.PAUSED)
            {
                Console.WriteLine($"[MANAGER] Seeking {time} : {DIDLInfo.GetDurationFromSeconds(time)}");
                _runtime.Seek(_device, DIDLInfo.GetDurationFromSeconds(time));
                await Task.Delay(100);
                while (_poller.PollNow() != cS)
                {
                    await Task.Delay(500);
                    if(DateTime.Now> timeout)
                    {
                        ct.Cancel();
                        return false;
                    }
                }
                return true;
            }
            return false;
        }
        public async Task<bool> PlaySonos(PlaybackInfo playbackInfo, string Ip, int port)
        {
            try
            {
                if (DoTransition(Transitions.PlayReq))
                {
                    ResetToken();
                    var streamurl = PlaybackStore.BuildInternalUri(Ip, port, playbackInfo.VideoId);
                    var didl = DIDLInfo.GetInfo(playbackInfo, streamurl);
                    _currentPlaying = new()
                    {
                        _playbackInfo = playbackInfo,
                        Didl = didl,
                        url = streamurl
                    };
                    Console.WriteLine($"[MANAGER] Preparing url for sonos player : {_currentPlaying} || {streamurl}");
                    PlayUrl();
                    var ct = new CancellationTokenSource();
                    _poller.PausePoll(10, ct.Token);
                    var timeout = 9;
                    var sTime = DateTime.Now + TimeSpan.FromSeconds(timeout);
                    while (!EnsureState(State.PLAYING))
                    {
                        await Task.Delay(500);
                        if (DateTime.Now > sTime)
                        {
                            var s = EnsureState(State.PLAYING);
                            ct.Cancel();
                            return s;
                        }
                    }
                    ct.Cancel();
                    Console.WriteLine($"[MANAGER] State is Playing: {_currentPlaying} || {streamurl}");
                    return true;
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[MANAGER] Tried playing url but failed: {ex.Message}");
            }
            return false;

        }
        public void ResumeSonos()
        {
            if (DoTransition(Transitions.PlayReq)) 
            {
                _runtime.Play(_device);
            }
        }
        public void PauseSonos()
        {
            if (DoTransition(Transitions.PauseReq))
            {
                _runtime.Pause(_device);
            }
        }
        public void StopSonos()
        {
            if (DoTransition(Transitions.StopReq))
            {
                _runtime.Stop(_device);
                _currentPlaying = null;
            }
        }
        public void SetVolume(Volume volume)
        {
            _runtime.SetVolume(_device, volume);
        }
        public Volume GetVolume()
        {
            return _runtime.GetVolume(_device);
        }
        public int GetPosition()
        {
            if(_cState== State.PLAYING || _cState== State.PAUSED)
            {
                return _runtime.GetPosition(_device);
            }
            return 0;
        }
        private void ResetToken()
        {
            if (_cancellationTokenSource.IsCancellationRequested && !_cancellationTokenSource.TryReset())
            {
                _cancellationTokenSource = new();
            }
        }
        private void PlayUrl()
        {
            if (_currentPlaying != null)
            {
                _runtime.PlayUrl(_device, _currentPlaying.url, _currentPlaying.Didl);
            }
        }
        
        
        public override void HandleBadLoad(IPoller poller)
        {
            var ct = new CancellationTokenSource();
            poller.PausePoll(10,ct.Token);
            try
            {
                var retry = 3;
                while (retry > 0 && !ShutdownToken.Token.IsCancellationRequested)
                {
                    Console.WriteLine($"[MANAGER] Retry to play Url on bad load detected: {retry} - {_currentPlaying?.url}");
                    retry--;
                    _cancellationTokenSource.Cancel();
                    Task.Delay(500).Wait();
                    ResetToken();
                    PlayUrl();
                    Task.Delay(1000).Wait();
                    if (poller.PollNow() != State.STOPPED)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MAANGER] Rectifying bad load failed with exception: {ex}");
            }
            finally { 
                ct.Cancel();
            }
        }

        public override void HandleTimeout(IPoller poller,State expected)
        {
            var cTime = DateTime.Now;
            while (DateTime.Now <= cTime + TimeSpan.FromSeconds(_timeoutDuration))
            {
                if (poller.PollNow() == expected)
                {
                    return;
                }
            }
            Console.WriteLine($"[MANAGER] Timed out while expecting {expected} state... cancelling");
            _cancellationTokenSource.Cancel();
        }
        public CancellationToken GetToken() {
            return _cancellationTokenSource.Token;
        }
    }
}
