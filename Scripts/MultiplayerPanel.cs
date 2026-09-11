using System.Linq;
using Godot;

// Host/join bench for the ENet transport and the roster in NetworkManager.
public partial class MultiplayerPanel : Control
{
	private LineEdit _name;
	private LineEdit _address;
	private Label _status;
	private Button _host;
	private Button _join;
	private Button _start;

	public override void _Ready()
	{
		MenuUtils.AttachButtonSounds(this);

		_name = (LineEdit)FindChild("NameEdit");
		_name.Text = ConfigFileHandler.Instance.LoadPlayerName();
		// Saved as it is typed: the name has to be on disk before Host or Join reads it.
		_name.TextChanged += ConfigFileHandler.Instance.SavePlayerName;

		_address = (LineEdit)FindChild("AddressEdit");
		_status = (Label)FindChild("StatusLabel");

		_host = (Button)FindChild("HostButton");
		_join = (Button)FindChild("JoinButton");
		_start = (Button)FindChild("StartButton");

		_host.Pressed += OnHost;
		_join.Pressed += OnJoin;
		_start.Pressed += OnStart;
		((Button)FindChild("BackButton")).Pressed += OnBack;

		NetworkManager net = NetworkManager.Instance;
		net.PeerJoined += OnPeerJoined;
		net.PeerLeft += OnPeerLeft;
		net.JoinedServer += OnJoinedServer;
		net.LeftServer += OnLeftServer;

		Refresh();
	}

	// NetworkManager outlives this panel, so its signals must not keep pointing here.
	public override void _ExitTree()
	{
		NetworkManager net = NetworkManager.Instance;
		if (net == null) return;

		net.PeerJoined -= OnPeerJoined;
		net.PeerLeft -= OnPeerLeft;
		net.JoinedServer -= OnJoinedServer;
		net.LeftServer -= OnLeftServer;
	}

	private void OnHost()
	{
		if (NetworkManager.Instance.Host())
			Refresh();
		else
			_status.Text = "could not host";
	}

	private void OnJoin()
	{
		string address = _address.Text.Length > 0 ? _address.Text : "127.0.0.1";
		if (NetworkManager.Instance.Join(address))
			_status.Text = $"connecting to {address}…";
		else
			_status.Text = $"could not reach {address}";
	}

	private void OnStart()
	{
		MatchManager.Instance.StartMatch();
	}

	private void OnBack()
	{
		NetworkManager.Instance.Leave();
		GetParent<MenuStack>().Pop();
	}

	private void OnPeerJoined(int peerId) => Refresh();
	private void OnPeerLeft(int peerId) => Refresh();
	private void OnJoinedServer() => Refresh();
	private void OnLeftServer(string reason) { Refresh(); _status.Text = reason; }

	private void Refresh()
	{
		NetworkManager net = NetworkManager.Instance;
		if (!net.IsActive)
		{
			_status.Text = "offline";
			_name.Editable = true;
			_start.Disabled = true;
			_host.Disabled = false;
			_join.Disabled = false;
			return;
		}

		string role = net.IsServer ? "hosting" : "client";
		_status.Text = $"{role} · {string.Join(", ", net.Peers.Select(net.NameOf))}";
		// Locked once connected: the roster already carries this name to everyone else.
		_name.Editable = false;

		// Only the host starts the match; clients are taken along by its RPC.
		_start.Disabled = !net.IsServer;
		// Already in a session: leave with BACK before opening another.
		_host.Disabled = true;
		_join.Disabled = true;
	}
}
