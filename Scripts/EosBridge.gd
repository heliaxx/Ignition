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

signal became_available()
## Each entry: {id: String, data: the lobby's attributes, key to value}
signal lobbies_found(lobbies: Array)
signal lobby_opened(host_product_user_id: String, is_host: bool)
signal lobby_failed(reason: String)
signal host_promoted(product_user_id: String, ok: bool)

## The lobby attribute an invite-only lobby is found by; LobbyInfo.CodeKey on the C# side.
const CODE_KEY := "CODE"

var available := false

var _platform_up := false

## EOS calls still waiting for an answer. Shutting the SDK down under one crashes it.
var _in_flight := 0

var _lobby: HLobby = null
var _found := {}
var _pending := {}
var _publishing := false


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
	_platform_up = true

	if not await _login_async():
		print("EosBridge: EOS login failed")
		return

	available = true
	print("EosBridge: EOS ready as %s" % HAuth.product_user_id)
	became_available.emit()

func _exit_tree() -> void:
	if not _platform_up:
		return

	_platform_up = false
	available = false
	# Bounded, so a call that never answers cannot hold the game open.
	var give_up := Time.get_ticks_msec() + 2000
	while _in_flight > 0 and Time.get_ticks_msec() < give_up:
		IEOS.tick()
		OS.delay_msec(10)
	EOS.Platform.PlatformInterface.release()
	EOS.Platform.PlatformInterface.shutdown()


## An empty code opens a lobby anyone can find.
func host_lobby(max_players: int, code: String) -> void:
	if not available:
		return

	var opts := EOS.Lobby.CreateLobbyOptions.new()
	opts.bucket_id = BUCKET_ID
	opts.max_lobby_members = max_players

	var lobby: HLobby = await _counted(HLobbies.create_lobby_async.bind(opts))
	if lobby == null:
		lobby_failed.emit("EOS could not create the lobby")
		return

	# Right away rather than with the first regular update, which would leave an invite-only
	# lobby listed for everyone in the meantime.
	if not code.is_empty():
		lobby.add_attribute(CODE_KEY, code)
		await _counted(lobby.update_async)

	_lobby = lobby
	lobby_opened.emit(HAuth.product_user_id, true)


func join_by_code(code: String) -> void:
	if not available:
		return

	var results = await _counted(HLobbies.search_by_attribute_async.bind([
		{key = EOS.Lobby.SEARCH_BUCKET_ID, value = BUCKET_ID},
		{key = CODE_KEY, value = code},
	]))
	if results == null or results.is_empty():
		lobby_failed.emit("no game with that code")
		return

	var lobby: HLobby = await _counted(HLobbies.join_async.bind(results[0]))
	if lobby == null:
		lobby_failed.emit("could not join that game")
		return

	_lobby = lobby
	lobby_opened.emit(lobby.owner_product_user_id, false)


## Owner only: makes another member the lobby's owner.
func promote(product_user_id: String) -> void:
	var member: HLobbyMember = _lobby.get_member_by_product_user_id(product_user_id) if _lobby != null else null
	var ok := false
	if member != null:
		ok = await _counted(member.promote_member_async)
	host_promoted.emit(product_user_id, ok)


func join_lobby(lobby_id: String) -> void:
	if not available:
		return

	var found: HLobby = _found.get(lobby_id)
	if found == null:
		lobby_failed.emit("that game is gone")
		return

	var lobby: HLobby = await _counted(HLobbies.join_async.bind(found))
	if lobby == null:
		lobby_failed.emit("could not join that game")
		return

	_lobby = lobby
	lobby_opened.emit(lobby.owner_product_user_id, false)


func refresh_lobbies() -> void:
	if not available:
		return

	var results = await _counted(HLobbies.search_by_bucket_id_async.bind(BUCKET_ID))
	_found.clear()
	var lobbies := []
	for lobby in results if results != null else []:
		_found[lobby.lobby_id] = lobby
		var data := {}
		for attribute in lobby.attributes:
			data[attribute.key] = str(attribute.value)
		lobbies.append({id = lobby.lobby_id, data = data})

	lobbies_found.emit(lobbies)


## Rewrites the lobby's attributes. Calls that land while an update is still in flight are
## folded into the next one, so only the latest data goes out.
func publish(data: Dictionary) -> void:
	if _lobby == null or not _lobby.is_owner():
		return

	_pending = data
	if _publishing:
		return

	_publishing = true
	while not _pending.is_empty() and _lobby != null:
		var next := _pending
		_pending = {}
		for key in next:
			_lobby.add_attribute(key, next[key])
		await _counted(_lobby.update_async)
	_publishing = false


func leave_lobby() -> void:
	if _lobby == null:
		return

	var lobby := _lobby
	_lobby = null
	if lobby.is_owner():
		await _counted(lobby.destroy_async)
	else:
		await _counted(lobby.leave_async)

## Runs an EOS call that answers later, counted so that shutting down can wait for it.
func _counted(request: Callable):
	_in_flight += 1
	var result = await request.call()
	_in_flight -= 1
	return result


func _login_async() -> bool:
	if await HAuth.login_game_services_async(_login_options()):
		return true

	EOS.Connect.ConnectInterface.create_device_id(_device_id_options())
	var created = await IEOS.connect_interface_create_device_id_callback
	if not EOS.is_success(created):
		print("EosBridge: could not create an EOS device id: %s" % EOS.result_str(created.result_code))
		return false

	return await HAuth.login_game_services_async(_login_options())


func _login_options() -> EOS.Connect.LoginOptions:
	var opts := EOS.Connect.LoginOptions.new()
	opts.credentials = EOS.Connect.Credentials.new()
	opts.credentials.type = EOS.ExternalCredentialType.DeviceidAccessToken
	opts.credentials.token = null
	opts.user_login_info = EOS.Connect.UserLoginInfo.new()
	opts.user_login_info.display_name = _player_name()
	return opts


func _device_id_options() -> EOS.Connect.CreateDeviceIdOptions:
	var opts := EOS.Connect.CreateDeviceIdOptions.new()
	opts.device_model = " ".join(PackedStringArray([OS.get_name(), OS.get_model_name()]))
	return opts


## The secret lives in an untracked script holding only CLIENT_SECRET, so a clone of this
## repository carries no credentials. Exports include it like any other script.
func _client_secret() -> String:
	if not ResourceLoader.exists(SECRET_PATH):
		return ""
	return load(SECRET_PATH).get_script_constant_map().get("CLIENT_SECRET", "")


func _player_name() -> String:
	return get_node("/root/ConfigFileHandler").LoadPlayerName()
