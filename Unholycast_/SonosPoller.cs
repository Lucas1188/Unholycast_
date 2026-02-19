using Microsoft.AspNetCore.StaticAssets.Infrastructure;
using Python.Runtime;

namespace Unholycast_
{
    public enum SonosStates
    {
        None,
        Transition,
        PausedPlayback,
        Playing,
        Stopped,
        FAULT
    }
    public class SonosPoller : UnholyStateManager.IPoller
    {

        private readonly SocoRuntime? _runtime;
        private dynamic _device;
        private Task? Poller;
        private CancellationTokenSource? _tokenSource;
        public delegate void PollerStateHandler(SonosTransportState state,SonosTrackInfo trackinfo,Volume volume);
        public event PollerStateHandler OnPolledState;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public SonosPoller(SocoRuntime runtime, dynamic device)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        {
            if (runtime == null || device == null) throw new ArgumentNullException("[SonosPoller] Cannot initialize poller due to bad arguments");
            _runtime = runtime;
            _device = device!;
        }
        private bool shouldPoll = true;
        public async Task Begin()
        {
            Poller = StatePolling(ShutdownToken.Token);
            await Poller;
        }
        protected async Task StatePolling(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (shouldPoll)
                    {
                        if (_device == null)
                        {
                            OnPolledState?.Invoke(new(), new() ,new());
                            break;
                        }
                        var states = Poll();
                        OnPolledState?.Invoke(states.Item1, states.Item2,states.Item3);
                        await Task.Delay(1000);
                    }
                }
                Console.WriteLine("[Poller] Ended");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Poller] Encountered Errors: {ex}");
                OnPolledState?.Invoke(new(), new(), new());
            }
        }
        private (SonosTransportState,SonosTrackInfo ,Volume) Poll()
        {
            SonosTransportState _s;
            Volume _v = new();
            SonosTrackInfo _ti = new();
            _s = _runtime!.GetTransportState(_device); //_device.get_current_transport_info()["current_transport_state"];
            _v = _runtime.GetVolume(_device);
            _ti = _runtime.GetSonosTrackInfo(_device);
#if DEBUG
            //Console.WriteLine($"[Poller] Got State: {_s} {_v}");
#endif
            return (_s,_ti ,_v);
        }
        private SonosStates GetState(string result)
        {
            if (result == null) return SonosStates.FAULT;
            switch (result)
            {
                case "PLAYING":
                    return SonosStates.Playing;
                case "PAUSED_PLAYBACK":
                    return SonosStates.PausedPlayback;
                case "STOPPED":
                    return SonosStates.Stopped;
                case "TRANSITIONING":
                    return SonosStates.Transition;
                default:
                    return SonosStates.None;
            }
        }

        public UnholyStateManager.State PollNow()
        {
            var _s = GetState(Poll().Item1.current_transport_state!);
            switch (_s)
            {
                case SonosStates.Playing:
                    return UnholyStateManager.State.PLAYING;
                case SonosStates.PausedPlayback:
                    return UnholyStateManager.State.PAUSED;
                case SonosStates.Stopped:
                    return UnholyStateManager.State.STOPPED;
                case SonosStates.Transition:
                    return UnholyStateManager.State.LOADING;
                default:
                    return UnholyStateManager.State.FAULT;
            }
        }

        public void PausePoll(int seconds, CancellationToken cancellationToken)
        {
            shouldPoll = false;
            _ = Task.Run(() =>
            {
                Console.WriteLine($"[Poller] Called to pause for {seconds} seconds");
                Task.Delay(seconds * 1000, cancellationToken);
                shouldPoll = true;
                Console.WriteLine($"[Poller] Resume polling from pause of {seconds} seconds");
            });
        }
    }
    
}
