extends Node
## EOS transport: logs in, owns the lobby a session runs in, and says who
## hosts it. GDScript because the EOS addon's high level API is asynchronous GDScript that
## C# cannot await; EosManager drives this and opens the peer.

const PRODUCT_NAME := "Ignition"
const PRODUCT_VERSION := "1.0"
const PRODUCT_ID := "3fc2025cf7de4448a4f51990ba1f0804"
const SANDBOX_ID := "59c61985c1f3494f8bfddd6db5ed5a18"
const DEPLOYMENT_ID := "5ad76308d17445ac82c266f3b404e469"
const CLIENT_ID := "xyza7891YFkzkBWXP28ZxCNSuMSIWWkU"

## The client secret is kept out of the repository; see SECRET_PATH.
const SECRET_PATH := "res://Scripts/EosSecret.gd"

## Lobbies are searched by bucket, so only this game's turn up.
const BUCKET_ID := "ignition-pvp"

## EOS upper-cases attribute keys, so searches only match when we ask for it that way.
const HOST_KEY := "HOST"

signal became_available()
## Each entry: {id: String, host: String, players: int}
signal lobbies_found(lobbies: Array)
signal lobby_opened(host_product_user_id: String, is_host: bool)
signal lobby_failed(reason: String)

var available := false

var _lobby: HLobby = null
var _found := {}


func _ready() -> void:
	var creds := HCredentials.new()
	creds.product_name = PRODUCT_NAME
	creds.product_version = PRODUCT_VERSION
	creds.product_id = PRODUCT_ID
	creds.sandbox_id = SANDBOX_ID
	creds.deployment_id = DEPLOYMENT_ID
	creds.client_id = CLIENT_ID
	creds.client_secret = _client_secret()
	if creds.client_secret.is_empty():
		print("EosBridge: no client secret at %s; EOS multiplayer is off" % SECRET_PATH)
		return

	if not await HPlatform.setup_eos_async(creds):
		print("EosBridge: EOS unavailable")
		return

	# Device id login: no EOS account, no login screen, just this machine.
	if not await HAuth.login_anonymous_async(_player_name()):
		print("EosBridge: EOS login failed")
		return

	available = true
	became_available.emit()


func host_lobby(max_players: int) -> void:
	if not available:
		return

	var opts := EOS.Lobby.CreateLobbyOptions.new()
	opts.bucket_id = BUCKET_ID
	opts.max_lobby_members = max_players

	var lobby: HLobby = await HLobbies.create_lobby_async(opts)
	if lobby == null:
		lobby_failed.emit("EOS could not create the lobby")
		return

	_lobby = lobby
	lobby.add_attribute(HOST_KEY, _player_name())
	await lobby.update_async()
	lobby_opened.emit(HAuth.product_user_id, true)


func join_lobby(lobby_id: String) -> void:
	if not available:
		return

	var found: HLobby = _found.get(lobby_id)
	if found == null:
		lobby_failed.emit("that game is gone")
		return

	var lobby: HLobby = await HLobbies.join_async(found)
	if lobby == null:
		lobby_failed.emit("could not join that game")
		return

	_lobby = lobby
	lobby_opened.emit(lobby.owner_product_user_id, false)


func refresh_lobbies() -> void:
	if not available:
		return

	var results = await HLobbies.search_by_bucket_id_async(BUCKET_ID)
	_found.clear()
	var lobbies := []
	for lobby in results if results != null else []:
		_found[lobby.lobby_id] = lobby
		var host = lobby.get_attribute(HOST_KEY)
		lobbies.append({
			id = lobby.lobby_id,
			host = host.value if host.has("value") else "?",
			players = lobby.max_members - lobby.available_slots,
		})

	lobbies_found.emit(lobbies)


func leave_lobby() -> void:
	if _lobby == null:
		return

	var lobby := _lobby
	_lobby = null
	if lobby.is_owner():
		await lobby.destroy_async()
	else:
		await lobby.leave_async()


## The secret lives in an untracked script holding only CLIENT_SECRET, so a clone of this
## repository carries no credentials. Exports include it like any other script.
func _client_secret() -> String:
	if not ResourceLoader.exists(SECRET_PATH):
		return ""
	return load(SECRET_PATH).get_script_constant_map().get("CLIENT_SECRET", "")


func _player_name() -> String:
	return get_node("/root/ConfigFileHandler").LoadPlayerName()
