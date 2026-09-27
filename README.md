# Learn Photon Fusion by building a multiplayer VR game

This tutorial introduces multiplayer networking through a small Unity VR project. Two players meet in a lobby, enter a game together, see each other’s avatars, talk, and interact with shared cubes. Basic Unity and C# knowledge helps; no previous Photon experience is needed.

**Photon Fusion** shares game state, **Photon Voice** carries speech, and **XR Interaction Toolkit (XRI)** handles VR tracking and interactions. Each section explains the idea, the Unity setup, and the code behind it.

![The Game scene](docs/images/game-overview.png)

## 1. Understand what networking shares

Each device runs its **own copy** of the scene and scripts. Networking shares selected information—positions, colors, or actions—so those copies show a consistent world. **Local** means your device; **remote** means another player’s device. A connected device is also called a **client** or **peer**.

This project uses Fusion’s **Shared mode**. Each player controls their avatar. Fusion calls the permission to update a network object **state authority**. One player also becomes the **Shared master client**, responsible here for the countdown and scene changes. Another player takes over that role if the master leaves. Shared mode does not make that player a server hosting everyone’s simulation.

Our default room holds **two players**. The first waits in Lobby; the second fills the room and starts a **five-second countdown**. Both then enter Game, and admission closes. If one leaves, the remaining player returns to Lobby and the room reopens.

The scripts follow Photon’s [Shared Mode Basics](https://doc.photonengine.com/fusion/v2/tutorials/shared-mode-basics/2-scene-and-player), extended with VR and a waiting room.

## 2. Connect to a room

**The idea:** a `NetworkRunner` manages this device’s connection and network updates. Calling `StartGame` in Shared mode creates or joins the named room. Our Lobby Scene is a waiting area inside that room, separate from Photon’s matchmaking lobby used to discover sessions.

**Unity setup:** this repository uses Unity **6000.3.23f1**, Fusion **2.1.3**, Voice **2.63.0**, and XRI **3.3.2**. Fusion is included; Package Manager resolves the other packages.

1. Open the project in Unity Hub and allow importing to finish.
2. Create a **Fusion** application in the [Photon Dashboard](https://dashboard.photonengine.com/). Put its ID into **App Id Fusion** on `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`. For speech, create a separate **Voice** application and fill **App Id Voice**.
3. Open `Assets/Scenes/Lobby Scene.unity`. In **Build Profiles → Scene List**, keep Lobby at index `0` and Game at `1`. These numbers identify the scenes to load.
4. On **Fusion Network Manager**, use `NetworkRunner`, `NetworkEvents`, `NetworkSceneManagerDefault`, `FusionNetworkManager`, and `FusionPlayerSpawner`. Assign the Network Player prefab. The supplied scene already has these assignments.

Read [FusionNetworkManager.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionNetworkManager.cs). Its connection call follows this pattern:

```csharp
var result = await runner.StartGame(new StartGameArgs {
    GameMode = GameMode.Shared,
    SessionName = SessionName,
    PlayerCount = MaxPlayers,
    Scene = SceneRef.FromIndex(0),
    SceneManager = GetComponent<NetworkSceneManagerDefault>()
});
```

`SessionName` identifies the room; `PlayerCount` sets its capacity. This code runs inside an **`async` method**: `await` lets Unity keep running while the connection completes. `result.Ok` tells us whether startup succeeded.

**Callbacks** are methods called when an event happens. The manager listens directly to Fusion’s events:

```csharp
var events = GetComponent<NetworkEvents>();
events.OnConnectedToServer.AddListener(r => SetStatus("Connected. Joining room..."));
events.OnShutdown.AddListener((r, reason) => Reconnect($"Runner stopped: {reason}."));
```

The full script also handles joins, departures, and connection failures. A small retry helper waits five seconds and reloads Lobby with a **new runner**: a stopped runner cannot be reused. Retrying joins again; it does not restore the old player identity or bypass a closed room.

## 3. Give everyone the same countdown

**The idea:** one authority decides when to start; the other devices receive its countdown. Otherwise, separate local timers could send players into Game at different times.

Add **Fusion Lobby State** with `NetworkObject` and [FusionLobbyState.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionLobbyState.cs). A `NetworkObject` gives an object a network identity. The script makes this object follow the Shared master when that role changes.

```csharp
[Networked] public TickTimer Countdown { get; set; }
```

`[Networked]` shares a property’s current value. `TickTimer` stores the countdown’s deadline; each device calculates the remaining time. Only the object’s state authority writes it.

Follow the script in this order:

1. `Spawned()` prepares the object when Fusion creates its network copy.
2. `PlayerJoined`, `PlayerLeft`, and `StateAuthorityChanged` update the starting-position slots through `RefreshSlots`.
3. `FixedUpdateNetwork()` checks player count and timer expiry on Fusion’s simulation ticks—not Unity’s `FixedUpdate` frames.
4. At expiry, the scene authority closes `SessionInfo.IsOpen` and calls `Runner.LoadScene(...)`. `NetworkSceneManagerDefault` coordinates loading for everyone.

Keep **Max Players = 2**, **Minimum Players = 2**, **Waiting Delay = 30**, and **Full Room Delay = 5**. For the **four-player example**, change Max Players to `4` before creating a fresh room: two players start 30 seconds; filling the room shortens the remaining time to at most five seconds. Falling below two cancels it.

For the return trip, [FusionPlayerSpawner.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionPlayerSpawner.cs) handles `PlayerLeft`: if only one remains, the scene authority loads Lobby. A small readiness check also covers departures during loading or master transfer. `LobbyReady` reopens admission. `FusionLobbyUI` displays connection status, player count, and remaining time.

## 4. Spawn an avatar and synchronize its appearance

**The idea:** your local XR rig follows your headset and controllers. A separate network avatar shares those movements with other players. A **prefab** is the reusable template from which Fusion creates that avatar.

Keep an **XR Origin (XR Rig)** in both scenes with working OpenXR/input settings. The avatar script looks for these exact paths:

```text
XR Origin (XR Rig)/Camera Offset/
    Main Camera
    Left Controller
    Right Controller
```

Configure the **Network Player** prefab:

| Component or setting | Why it is needed |
| --- | --- |
| `NetworkObject` | Network identity. Enable **Destroy When State Authority Leaves**; disable **Allow State Authority Override** for a personal avatar. |
| `NetworkTransform` | Shares position and rotation. Add it to the root and independently moving tracked parts. |
| `NetworkMecanimAnimator` | Shares hand animation parameters. Assign each hand’s **Animator**, not an animation clip. |
| `FusionNetworkPlayer` | Assign head, hands, torso/neck, ground contact, hand animators, and grip/trigger actions. |

Check the prefab appears in Fusion’s Network Project Config object table; rebuild the table if needed. This lets other devices find the same prefab.

The spawner creates **only our own avatar** on this device:

```csharp
var avatar = Runner.Spawn(PlayerPrefab, CameraRig.transform.position, CameraRig.transform.rotation);
Runner.SetPlayerObject(Runner.LocalPlayer, avatar);
```

`Runner.Spawn` creates a network object; `SetPlayerObject` associates it with our `PlayerRef` (player identifier). Other scripts can then find it with `Runner.GetPlayerObject`. Use `Runner.Despawn` to remove a network object.

In [FusionNetworkPlayer.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionNetworkPlayer.cs), only `HasStateAuthority` allows this device to copy its headset/controller input into the avatar. Without that check, our input could move local copies of other players’ avatars too.

```csharp
[Networked, OnChangedRender(nameof(ApplyPlayerColor))]
public Color PlayerColor { get; set; }
```

This is a C# **property**, using `{ get; set; }`. Fusion shares its value; `OnChangedRender` names the method that updates its visible material. `nameof(ApplyPlayerColor)` supplies the method’s name. We also call `ApplyPlayerColor()` in `Spawned`, because the change callback does not run for initial spawning.

**Across scenes:** the spawner remembers the network-assigned slot, keeping starting positions spaced 0.7 metres apart. It removes the old avatar and creates another against the new scene’s XR rig. The unchanged player identifier produces the same torso color. Live movement uses `NetworkTransform`; each new scene starts from its assigned position, not the last walked-to coordinate.

## 5. Add voice

Fusion shares gameplay data; Photon Voice sends audio. On the manager, add `FusionVoiceClient`, `Recorder`, `VoiceLogger`, and `FusionVoiceSetup`.

1. On **Recorder**, select **Microphone** and enable **Transmit Enabled**.
2. On **FusionVoiceClient**, enable **Use Fusion App Settings** and **Use Primary Recorder**. Drag the Recorder into **Primary Recorder**.
3. Assign **Speaker Prefab** to an object containing `Speaker` and `AudioSource`. The supplied scene uses the Speaker child from the avatar prefab as its template.

The **Recorder captures** your speech; the **Speaker plays** another player’s speech. `FusionVoiceSetup` handles microphone permission and recording readiness. This is room-wide voice; attaching speech to an avatar’s mouth requires a positional-voice setup. [Photon Voice setup](https://doc.photonengine.com/voice/v2/getting-started/voice-for-fusion)

## 6. Grab a shared cube: transfer authority

![The two cube examples with their saved materials](docs/images/cube-lessons.png)

**The idea:** the device holding the cube needs permission to update its position. This is an **authority transfer**.

On **Grabbable Cube**, use a collider, `Rigidbody`, `XRGrabInteractable`, `XRGeneralGrabTransformer`, `NetworkObject`, `NetworkTransform`, and [XRGrabNetworkInteractable.cs](Assets/Scripts/XRGrabNetworkInteractable.cs). Enable **Allow State Authority Override** and disable **Destroy When State Authority Leaves**.

XRI’s `selectEntered` event means a hand selected the cube. The script requests authority when needed:

```csharp
if (HasStateAuthority) return;
awaitingAuthority = true;
Object.RequestStateAuthority();
```

XRI moves the held cube; `NetworkTransform` sends the authority’s pose to other devices. `selectExited` releases authority after the last local hand lets go.

**A request takes time; it is not an immediate grant.** `StateAuthorityChanged` handles a grant arriving after a quick release. One peer controls the cube at a time. Start with one grabber at a time: simultaneous grabs and networked throwing need more handling than this teaching example provides.

## 7. Change a cube’s color: send an RPC

**The idea:** an **RPC (remote procedure call)** asks selected devices to run a method. A networked property shares “the color is blue”; an RPC sends “perform this color-changing action now.”

On **Interactable Cube**, use a collider, `XRSimpleInteractable`, `NetworkObject`, and [XRSimpleNetworkInteractable.cs](Assets/Scripts/XRSimpleNetworkInteractable.cs). Assign its renderer, text prompt, and particle system. Keep the object alive when its authority leaves.

`hoverEntered` finds our avatar using `Runner.GetPlayerObject(Runner.LocalPlayer)`, reads its `PlayerColor`, and calls:

```csharp
[Rpc(RpcSources.All, RpcTargets.All)]
private void RpcSetColor(Color color)
{
    if (targetRenderer) targetRenderer.material.color = color;
    if (txtInfo) txtInfo.text = color.Equals(_originalColor) ? "Tap the cube!" : "";
}
```

`RpcSources.All` allows any peer with the object to send; `RpcTargets.All` runs the method on current peers. This RPC needs no authority transfer. RPC names must have an `Rpc` prefix or suffix.

Everyone sees the cube match the player’s torso and its prompt disappear. The last local `hoverExited` restores the original color and prompt. Hover can come from a ray as well as a nearby hand.

Particles run only in **local hover handlers**, outside the RPC, so only the hovering player sees them. RPCs do not preserve history: a later arrival will not receive an old color-changing message. Use a networked property when the result must persist. Overlapping hovers in this simple example use the last received event.

## Keep these distinctions in mind

| Name | Purpose |
| --- | --- |
| `using Fusion;` | Makes Fusion’s C# types available. |
| `MonoBehaviour` | Ordinary Unity logic: connection setup, UI, and permissions. |
| `SimulationBehaviour` | Runner/session callbacks, used by the spawner. |
| `NetworkBehaviour` | Object-level callbacks, networked properties, and RPCs. |
| `Awake` / `Start` | Unity initialization callbacks. |
| `Spawned` / `Despawned` | Fusion callbacks when a network copy appears or is removed. |
| `HasStateAuthority` / `IsSceneAuthority` | Permission to update an object / coordinate scene changes. |

Fusion’s **weaver** generates the synchronization code behind `[Networked]` and `[Rpc]`. Keep `Assembly-CSharp` in Network Project Config’s assemblies-to-weave list for these scripts.

## Test as you learn

Build and run two clients with working VR input. Start both in Lobby and use matching App IDs, region, app version, session name, timer settings, scene indices, and spacing. Gameplay settings are local Inspector configuration; the countdown and slots are networked state.

Check one feature at a time: **join → countdown → avatars → voice → grab → hover → leave and return**. If players wait alone, compare their connection settings. If voice is silent, check the Voice ID, permission, Recorder, and Speaker assignments.

Screenshots are offline Unity scene previews using the saved materials, not multiplayer recordings. See [image sources](docs/images/README.md) for capture details and a [close-up of the interactable cube](docs/images/interactable-cube.png).
