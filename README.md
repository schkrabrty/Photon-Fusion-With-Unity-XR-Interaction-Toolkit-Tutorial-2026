# Learn Photon Fusion with a small multiplayer VR game

You know PUN 2 and want to write Fusion scripts yourself. Start with this story: **connect → meet in a lobby → enter Game together → see, hear, and interact with each other**. First understand why each step exists, then follow its script and Unity setup.

This project follows the local-player spawning pattern in Photon's [Shared Mode Basics](https://doc.photonengine.com/fusion/v2/tutorials/shared-mode-basics/2-scene-and-player), extended with XR, a waiting room, and two cube lessons.

![The Game scene and its interaction table](docs/images/game-overview.png)

*Scene images are offline Unity previews, not recordings of a multiplayer test.*

## 1. The story before the scripts

Each device runs a **NetworkRunner**, which manages that device's Fusion connection and simulation. `StartGame` in **Shared** mode creates or joins the named session. Our Lobby Scene is a waiting area **inside that session**; it is different from Photon's matchmaking lobby.

Shared mode distributes **state authority** across players: you control your avatar, and another player controls theirs. The **Shared master client** also manages scene changes and our countdown. It is not a player-hosted server. In Host mode, a player-host owns the authoritative simulation; in Server mode, a dedicated server does. [Runner](https://doc.photonengine.com/fusion/v2/manual/network-runner) · [Shared master](https://doc.photonengine.com/fusion/v2/manual/shared-mode-master-client)

The current game has **two places**. One player waits; the second fills the room, so the countdown immediately shortens to **five seconds**. For the four-player example, change Max Players to `4`: two players start a 30-second countdown, and filling all four places shortens the remaining time to at most five seconds. Falling below two cancels the countdown. At zero, admission closes and everyone loads Game. Game continues with two or more players; the last remaining player returns to Lobby, which reopens for new arrivals.

Room capacity is our gameplay choice. **CCU means concurrent users across the application, not players per room.** Photon currently offers a free 100-CCU Fusion games plan with eligibility conditions; check [current pricing](https://www.photonengine.com/Fusion/Pricing). The script's 20-player cap is a tutorial setting, not a claim about the free tier.

## 2. Install, configure, and connect

The repository uses Unity **6000.3.23f1**, Fusion **2.1.3**, Voice **2.63.0**, and XRI **3.3.2**. Fusion is included; Package Manager resolves Voice. In a fresh project, install Fusion 2 and its Voice integration, plus XRI, its Starter Assets, Input System, and OpenXR. Test local XR tracking first.

1. Create a **Fusion** application in the [Photon Dashboard](https://dashboard.photonengine.com/). Enter its ID in **App Id Fusion** on `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`.
2. For speech, create a separate **Voice** application and fill **App Id Voice**. An empty Voice ID disables voice startup here.
3. Keep **Lobby Scene** at build index `0` and **Game Scene** at `1` in **Build Profiles → Scene List**. Start through Lobby.
4. Put an **XR Origin (XR Rig)** in each scene, with enabled input actions and the target platform's OpenXR configuration. Keep the hierarchy names below: the avatar finds these transforms at runtime.

```text
XR Origin (XR Rig)/Camera Offset/
    Main Camera
    Left Controller
    Right Controller
```

On **Fusion Network Manager**, use `NetworkRunner`, `NetworkEvents`, `NetworkSceneManagerDefault`, `FusionNetworkManager`, and `FusionPlayerSpawner`. Assign the Network Player prefab. On the waiting-room panel, `FusionLobbyUI` holds the three TMP labels and lobby camera/canvas references. The supplied scene has these assignments and enables the count and timer labels.

Set **Max Players = 2**, **Minimum Players = 2**, **Waiting Delay = 30**, **Full Room Delay = 5**, and **Game Scene Build Index = 1**. Clients must share the App ID, app version, region, and session name (`Room 1`). Change capacity before creating a fresh session. Use the same minimum, timer delays, scene indices, and spawn spacing on every client: these tutorial configuration values are local Inspector settings, not extra networked properties.

Read [FusionNetworkManager.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionNetworkManager.cs):

```csharp
var result = await _runner.StartGame(new StartGameArgs {
    GameMode = GameMode.Shared,
    SessionName = SessionName,
    PlayerCount = MaxPlayers,
    Scene = SceneRef.FromIndex(0),
    SceneManager = GetComponent<NetworkSceneManagerDefault>()
});
```

This shortened excerpt shows the idea; the full script validates settings and checks `result.Ok`. **`async`/`await` lets Unity continue running while connection work finishes.** `OnConnectedToServer` reports the transport connection; successful `StartGame` confirms startup. `NetworkEvents` listeners directly handle connection status, joins, departures, failures, disconnects, and shutdown. A small custom retry helper waits five seconds and reloads Lobby with a **new runner** because a stopped runner cannot be reused. It retries joining; it does not restore a disconnected player's old identity or bypass a closed room.

## 3. Wait together, then change scenes together

Add a scene object called **Fusion Lobby State**, with `NetworkObject` and [FusionLobbyState.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionLobbyState.cs). The script makes it a **Master Client Object** and keeps it alive if its authority leaves. Only its state authority writes the spawn-slot table and `TickTimer`; Fusion replicates those two properties to everyone. `IPlayerJoined`, `IPlayerLeft`, and `IStateAuthorityChanged` call one small `RefreshSlots` helper on membership or authority changes, instead of rebuilding the slots every tick. `Spawned` also initializes slots for players already present.

![Shared countdown rules from the source](docs/images/code-countdown.png)

`FixedUpdateNetwork()` counts `Runner.ActivePlayers` and applies the rules from step 1. `TickTimer` shares an expiry tick, so every client displays the same deadline. The scene authority closes `SessionInfo.IsOpen`, hides the session, and uses **`Runner.LoadScene`**, with `NetworkSceneManagerDefault`, to load Game for everyone.

For the return trip, read `PlayerLeft → TryReturnToLobby` in [FusionPlayerSpawner.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionPlayerSpawner.cs). If only one player remains, the scene authority loads Lobby. `LobbyReady` reopens admission after the lobby state spawns. A small `Update` check covers departures during loading and master transfer that finishes after `PlayerLeft`; a departure callback alone can miss those cases. These helpers express our game rules—Fusion does not supply a built-in “return when one player remains” event.

The persistent manager keeps the connection across scenes and removes the duplicate manager when Lobby returns. The new scene’s `FusionLobbyUI` reads the surviving manager directly; there is no UI-reference copying. Network events report changes, but the countdown still needs `FixedUpdateNetwork` to notice its deadline.

## 4. Give each player an avatar and a recognizable color

A **NetworkObject** gives an object a network identity; it does not automatically synchronize every component. On the **Network Player** prefab, configure:

| Component or setting | Purpose |
| --- | --- |
| `NetworkObject` | Enable **Destroy When State Authority Leaves**; disable **Allow State Authority Override** for this personal avatar. |
| `NetworkTransform` | On the root and independently moving tracked parts; replicates their poses. |
| `NetworkMecanimAnimator` | On each animated hand; assign that hand's **Animator**, not an animation clip. |
| `FusionNetworkPlayer` | Assign head, hands, neck/torso, ground contact, hand animators, and grip/trigger actions. |

Register the prefab in Fusion's object table (rebuild it if needed). Fusion's **weaver** generates networking code for `[Networked]` and `[Rpc]`; ensure the gameplay assembly is in Network Project Config's assemblies-to-weave list. [NetworkObject setup](https://doc.photonengine.com/fusion/v2/manual/network-object)

The spawner follows the Basics tutorial: **spawn only for `Runner.LocalPlayer`**, then call `Runner.SetPlayerObject` so other scripts can find that avatar. Lobby assigns unique slots, 0.7 metres apart. Existing players keep their slots as others join or leave. `SceneLoadStart` despawns the old avatar; after loading, the spawner binds a new avatar to the new scene's XR rig.

**What survives the Lobby → Game transition?** The persistent spawner remembers its slot received from networked lobby state; spawn spacing comes from the shared tutorial configuration. The new avatar derives the same torso color from the unchanged `PlayerRef` and publishes it again. Starting positions use the same slot relative to each scene's rig; live movement is synchronized by `NetworkTransform`. We reset to a starting pose, rather than carrying a player's last walked-to world coordinate into a different scene. On a later return to Lobby, slots are assigned afresh.

Read [FusionNetworkPlayer.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionNetworkPlayer.cs):

```csharp
[Networked, OnChangedRender(nameof(ApplyPlayerColor))]
public Color PlayerColor { get; set; }
```

`Spawned()` sets the owner's color and finds the local XR rig. `FixedUpdateNetwork()` copies tracking and hand inputs **only when `HasStateAuthority` is true**. Otherwise a client could move its local copies of everyone else's avatars using its own headset. Torso and ground-contact offsets follow the head; the local avatar's renderers are hidden to avoid duplicate visuals.

`OnChangedRender` is an **attribute naming a render callback**, not a function to call for synchronization. Fusion replicates `PlayerColor`; `ApplyPlayerColor` draws it. We also call that method in `Spawned` because `OnChangedRender` does not run for initial spawning. [Change detection](https://doc.photonengine.com/fusion/v2/manual/data-transfer/change-detection)

## 5. Hear the other players

On the manager, add `FusionVoiceClient`, `Recorder`, `VoiceLogger`, and `FusionVoiceSetup`. The helper keeps platform permission code out of the connection lesson. Enable **Use Fusion App Settings**, assign the Recorder to **Primary Recorder**, enable **Use Primary Recorder**, and assign a **Speaker Prefab** containing `Speaker` and `AudioSource`. This scene references the Speaker child from the avatar prefab as the template.

The **Recorder captures and transmits** your microphone. The **Speaker receives and plays** somebody else's voice; it does not capture speech. This project uses room-wide voice from the persistent manager, not an avatar-bound positional voice setup. `FusionVoiceSetup` joins Voice when configured and records after microphone permission and room readiness. Enable **Transmit Enabled**; check voice detection if quiet speech is suppressed. Configure the platform's microphone usage description where required. [Voice integration](https://doc.photonengine.com/voice/v2/getting-started/voice-for-fusion)

## 6. Grab a cube: transfer state authority

![The Game scene's two cube lessons](docs/images/cube-lessons.png)

On **Grabbable Cube**, use a collider, `Rigidbody`, `XRGrabInteractable`, `XRGeneralGrabTransformer`, `NetworkObject`, `NetworkTransform`, and [XRGrabNetworkInteractable.cs](Assets/Scripts/XRGrabNetworkInteractable.cs). Enable **Allow State Authority Override** and disable **Destroy When State Authority Leaves**.

XRI's `selectEntered` requests authority **if we do not have it**. XRI moves the held cube, and `NetworkTransform` publishes the authority's pose. `selectExited` releases authority when the last local hand lets go. `StateAuthorityChanged` also releases a delayed grant if the hand has already let go.

**Requesting authority is asynchronous.** One peer has authority at a time; an unheld cube is not owned by every player. With override disabled, a new owner needs the current owner to release first—an approval UI would be custom game logic. Start this lesson with one grabber at a time: simultaneous grabs and authoritative throwing/physics need more handling than this short example provides. [Authority rules](https://doc.photonengine.com/fusion/v2/manual/network-object)

## 7. Touch a cube: send an RPC, keep particles local

On **Interactable Cube**, use a collider, `XRSimpleInteractable`, `NetworkObject`, and [XRSimpleNetworkInteractable.cs](Assets/Scripts/XRSimpleNetworkInteractable.cs). Keep it alive when its authority leaves. Assign **Target Renderer**, **Txt Info**, and **Touch Particles**. The prompt canvas uses `CanvasController` to face each client's camera locally.

`hoverEntered` finds our avatar with `Runner.GetPlayerObject(Runner.LocalPlayer)` and reads its **PlayerColor**. It sends that color through `RpcSetColor`; the last local `hoverExited` sends the original cube color. The RPC changes color and hides/restores the prompt on current peers. Hover may come from a ray as well as a nearby hand.

![The cube's All-to-All RPC](docs/images/code-rpc.png)

**RPC means remote procedure call.** `RpcSources.All` allows any peer with the object to send; `RpcTargets.All` runs it on current peers, including the sender by default. This RPC needs no authority request. Other filters include `StateAuthority`, `InputAuthority`, and `Proxies`; a proxy is a peer without the object's state or input authority, not a proxy server. Names must have an `Rpc` prefix or suffix. [RPC reference](https://doc.photonengine.com/fusion/v2/manual/data-transfer/rpcs)

Particles start/stop only inside local hover handlers. Because the RPC never controls them, other players do not see your effect. The color example deliberately uses **RPC-only visuals**: late joiners do not receive past RPCs, and overlapping players' interactions use the last received event. For persistent, arbitrated interactions, send requests to the authority and store the result in networked state. An optional [CubeColorLesson.cs](docs/examples/CubeColorLesson.cs) demonstrates that pattern; it is not installed on either cube.

## Fusion vocabulary to keep beside the scripts

| Name | Meaning here |
| --- | --- |
| `using Fusion;` | Imports Fusion's C# types. It does not itself enable networking. |
| `MonoBehaviour` | Ordinary Unity logic; suitable for our connection manager and UI. |
| `SimulationBehaviour` | Runner-level callbacks without object state; used by the spawner. |
| `NetworkBehaviour` | Behaviour attached to a NetworkObject; supports networked properties, RPCs, and object callbacks. |
| `IPlayerJoined` / `IPlayerLeft` | Fusion calls `PlayerJoined` / `PlayerLeft` when session membership changes. |
| `ISceneLoadStart` / `ISceneLoadDone` | Fusion notifies the spawner of scene loading; these are not new player joins. |
| `Spawned` / `Despawned` | Object attachment/removal callbacks on local copies, including replicas. |
| `FixedUpdateNetwork` / `Render` | Simulation-tick logic / visual-frame logic. A network tick is not Unity's `FixedUpdate`. |
| `Runner.Spawn` / `Runner.Despawn` | Create/remove a network object; avoid ordinary Instantiate/Destroy for active network avatars. |
| `PlayerRef` / `ActivePlayers` | Session player identifier / current participants. `PlayerRef.None` means no player. |
| `HasStateAuthority` / `IsSceneAuthority` | May this peer write this object's state / manage synchronized scenes? |
| Input authority | Identifies the input provider, chiefly for Host/Server input simulation; this Shared XR example reads local input on the state authority. |
| `[Networked]` / `NetworkArray` | Replicated auto-properties / fixed-capacity replicated collection. Ordinary fields stay local. |
| `TickTimer` / `SceneRef` | A network-tick deadline / Fusion's scene identifier. |
| `DontDestroyOnLoad` | Unity persistence, not replication; keeps the manager alive across local scene changes. |

## Try the whole story

Use separate clients/devices with working XR input. These singleton scripts are not a multi-runner-in-one-scene harness.

| Test | Expected result |
| --- | --- |
| Default: A joins; then B | Spaced lobby avatars; `1 / 2` then `2 / 2`; a shared five-second timer. |
| Four-player example: set Max Players to `4` on all clients before creating the room | A and B start a 30-second timer. |
| Four-player example: C and D join before expiry | Existing avatars stay put; countdown shortens to at most five seconds. |
| Count drops below two | Countdown cancels; reaching two restarts it (five seconds at capacity, otherwise 30). |
| Timer ends | Everyone loads Game with matching torso colors and corresponding spawn slots; new joins are rejected. |
| Move, speak, grab, hover | Other clients see poses, hear speech, see cube motion and matching color; particles stay local. |
| Leave, including the master | Game continues with two or more; the last player returns to Lobby and can meet a new arrival. |
| Interrupt the initial connection | Status reports failure; automatic retries use a fresh runner after the delay. |

If clients wait alone, check App ID, region, version, session name, and whether Game has already closed admission. If avatars fail, check prefab registration, scene indices, and exact XR hierarchy names. For silence, check Voice ID, microphone permission, Recorder/Primary Recorder, Speaker Prefab, and audio output.

The small [FusionLobbyUI.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionLobbyUI.cs) and [FusionVoiceSetup.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionVoiceSetup.cs) handle presentation and microphone setup separately from networking.

[HandPresence.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/HandPresence.cs) handles the local hand/controller visuals. [Image sources and regeneration](docs/images/README.md) explain the screenshots.
