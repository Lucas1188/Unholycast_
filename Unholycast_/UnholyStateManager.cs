using Unholycast_;

public abstract class UnholyStateManager
{
    public UnholyStateManager(IPoller poller)
    {
        _cState = State.STOPPED;
        runtimeGraph = new();
        _poller = poller;
        //_poller.OnExternalStateChange += EnsureState;
        foreach (var sst in stateGraph)
        {
            var cState = sst.Key.Item1;
            var nState = sst.Key.Item2;
            var t = sst.Value;
            runtimeGraph.Add((cState, t), nState);
        }
    }

    public enum State
	{
		PLAYING,
		PAUSED,
		STOPPED,
		LOADING,
		FAULT
	}
	public enum Transitions
	{
		StopReq,
		PauseReq,
		PlayReq,
		ServingSignal,
		Faulted
	}
	private readonly Dictionary<(State, State), Transitions> stateGraph = new()
	{
        {(State.PLAYING,State.PLAYING),Transitions.PlayReq},
        {(State.STOPPED,State.STOPPED),Transitions.StopReq},
        {(State.LOADING,State.STOPPED),Transitions.StopReq},
        {(State.PLAYING,State.STOPPED),Transitions.StopReq},
        {(State.PLAYING,State.PAUSED),Transitions.PauseReq},
        {(State.STOPPED,State.LOADING),Transitions.PlayReq},
        {(State.LOADING,State.PLAYING),Transitions.ServingSignal},
        {(State.PAUSED,State.LOADING),Transitions.PlayReq},
        {(State.PAUSED,State.STOPPED),Transitions.StopReq},
    };

	private Dictionary<(State, Transitions), State> runtimeGraph;
	public delegate void UnholyStateChangeHandler(State oldState, State newState);
	public event UnholyStateChangeHandler? UnholyStateChange;
	protected State _cState;
	protected IPoller _poller;
	

	public bool DoTransition(Transitions transition)
	{
		if(transition== Transitions.Faulted)
		{
			UnholyStateChange?.Invoke(_cState, State.FAULT);
			_cState = State.FAULT;
			return true;
		}
		if (!runtimeGraph.ContainsKey((_cState,transition)))
		{
			Console.WriteLine($"[StateManager] Bad transition requested C:{_cState} T:{transition}");
			return false;
		}
		else
		{
			var nState = runtimeGraph[(_cState, transition)];
			UnholyStateChange?.Invoke(_cState, nState);
			_cState = nState;
			return true;
		}
	}
	public bool EnsureState(State expectedState)
	{
		_cState = PollNow();
		if(_cState == expectedState) return true;
		if (_cState == State.PLAYING && expectedState == State.PAUSED) {
			DoTransition(Transitions.PauseReq);
		}
        //if (_cState == State.LOADING && externalState == State.LOADING)
        //{
        //    HandleTimeout(_poller,externalState);
        //}
        if (_cState == State.LOADING && expectedState == State.STOPPED) {
			HandleBadLoad(_poller);
		}
		if(_cState == State.LOADING && expectedState == State.PLAYING)
		{
			DoTransition(Transitions.ServingSignal);
			return _cState == State.PLAYING;
		}
		return false;
	}
	public abstract void HandleBadLoad(IPoller poller);
    public abstract void HandleTimeout(IPoller poller, State expected);
	public State PollNow()
	{
		return _poller.PollNow();
	}

    public delegate void UnholyExternalStateChangedHandler(State state);
    public interface IPoller
    {
		public State PollNow();
		public void PausePoll(int seconds,CancellationToken cancellationToken);
    }
}

