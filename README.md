# Learn Photon Fusion by building a multiplayer VR game

Imagine opening a VR game, meeting a friend in a waiting room, and entering a shared play space together. You can see their hands move, hear them speak, and pass a cube between you. This project builds that experience with **Photon Fusion** for networking, **Photon Voice** for speech, and **XR Interaction Toolkit (XRI)** for VR interactions.

Follow that journey through the networking ideas, Unity setup, and scripts that make it work. Basic Unity and C# knowledge helps; no previous Photon experience is needed.

![The shared play space](docs/images/game-overview.png)

## 1. Give the game somewhere to connect

Before players can meet, their games need to connect to the same Photon application. Create a **Fusion** application in the [Photon Dashboard](https://dashboard.photonengine.com/) and put its ID into **App Id Fusion** in `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`. If you want voice chat, create a separate **Voice** application and fill **App Id Voice** too.

Open this project through Unity Hub and let importing finish. Fusion is included. The project uses Unity **6000.3.23f1**, Fusion **2.1.3**, Voice **2.63.0**, and XRI **3.3.2**.

Our game has two scenes: **Lobby Scene**, where players wait, and **Game Scene**, where they interact. Open `Assets/Scenes/Lobby Scene.unity`. In **Build Profiles → Scene List**, keep Lobby at index `0` and Game at `1`; the scripts use those indices when loading scenes.

Each player's device runs its own copy of Unity. Networking shares selected information so both see the same experience. We call our device **local** and the other player's device **remote**; each connected device is a **client** or **peer**.

To bring those clients into one room, we use the `NetworkRunner` component on **Fusion Network Manager**. It manages the local connection and network simulation. [FusionNetworkManager.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionNetworkManager.cs) starts it with a call like this:

```csharp
var result = await runner.StartGame(new StartGameArgs {
    GameMode = GameMode.Shared,
    SessionName = SessionName,
    PlayerCount = MaxPlayers,
    Scene = SceneRef.FromIndex(0),
    SceneManager = GetComponent<NetworkSceneManagerDefault>()
});
```

In **Shared mode**, this creates the named room if needed or joins it if it already exists. `SessionName` identifies the room and `PlayerCount` sets its capacity—**two players by default** here. The method is `async`: `await` allows Unity to keep running while connection work finishes. `result.Ok` tells us whether startup succeeded.

The manager is an ordinary Unity `MonoBehaviour`, initialized through `Awake` and `Start`. Its `NetworkEvents` component provides **callbacks**: methods invoked when something happens, such as connecting, a player joining, or a shutdown. The script listens directly to these events to update status and detect failures. If the connection fails, it waits five seconds and reloads Lobby with a new runner, because a stopped runner cannot be reused. This is a fresh join attempt, not restoration of the old player.

The manager also needs `NetworkSceneManagerDefault` and `FusionPlayerSpawner`, with the Network Player prefab assigned, as in the supplied scene. Our Lobby Scene is a waiting area **inside the room**; it is separate from Photon's matchmaking lobby used to discover rooms.

## 2. Wait for a friend, then leave together

The first player waits until their friend arrives, then both should see the same countdown. Independent timers could disagree, so one device must decide when to start and share that decision.

Fusion calls permission to update a network object's state **state authority**. In Shared mode, players can have authority over different objects. One player also becomes the **Shared master client**; this project gives that role responsibility for the waiting-room state and scene changes. Another player takes over if the master leaves. The master is not a server hosting everyone's simulation.

On **Fusion Lobby State**, a `NetworkObject` gives the object a network identity. [FusionLobbyState.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionLobbyState.cs) derives from `NetworkBehaviour`, allowing object-level Fusion callbacks and shared properties such as:

```csharp
[Networked] public TickTimer Countdown { get; set; }
```

`[Networked]` tells Fusion to synchronize this property's value. A `TickTimer` stores a deadline, so every device can calculate the time remaining. Only the object's state authority sets it; the script keeps that authority with the Shared master.

When Fusion makes the object ready, `Spawned()` initializes it. `PlayerJoined` and `PlayerLeft` callbacks update the player slots. Then `FixedUpdateNetwork()` checks the player count and deadline on Fusion's simulation ticks, which are distinct from Unity's `FixedUpdate` frames. Events tell us someone arrived; these checks implement our rule for when to begin.

Keep **Max Players = 2**, **Minimum Players = 2**, **Waiting Delay = 30**, and **Full Room Delay = 5**. The second player fills our default room, so the countdown becomes five seconds. To try the four-player example, create a fresh room with Max Players set to `4`: two players start a 30-second wait, and filling the room shortens the remaining time to at most five seconds. Dropping below two cancels the countdown.

`FusionLobbyUI` reads the connection status, player count, and shared timer for the waiting-room panel. At expiry, the scene authority closes admission and calls `Runner.LoadScene(...)`; `NetworkSceneManagerDefault` coordinates everyone's move into Game. Before that journey feels like multiplayer, though, we need to make the people in the room visible.

## 3. Turn tracked headsets into recognizable players

The **XR Origin** follows your headset and controllers locally. Your friend needs a visible body that follows those movements, so we create a separate **network avatar** from a prefab—a reusable object template.

A `NetworkObject` identifies the avatar but does not synchronize its movement or appearance. On the **Network Player** prefab, we choose what to share:

| Component | What it does here |
| --- | --- |
| `NetworkObject` | Identifies the avatar. Enable **Destroy When State Authority Leaves** and disable **Allow State Authority Override**, because it belongs to one player. |
| `NetworkTransform` | Shares position and rotation on the root and independently moving tracked parts. |
| `NetworkMecanimAnimator` | Shares hand animation parameters. Assign the hand's **Animator**, not an animation clip. |
| `FusionNetworkPlayer` | Connects the avatar's head, hands, torso, ground contact, and hand animations to local tracking and input. |

Assign those references and the grip/trigger actions on `FusionNetworkPlayer`. Both scenes need a working XR rig with these exact paths, which the script uses to find tracking objects:

```text
XR Origin (XR Rig)/Camera Offset/
    Main Camera
    Left Controller
    Right Controller
```

Check that the prefab appears in Fusion's Network Project Config object table; rebuild the table if needed. Other clients need to resolve the same prefab when it is spawned.

[FusionPlayerSpawner.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionPlayerSpawner.cs) derives from `SimulationBehaviour` to receive runner/session callbacks. On each device it creates only that player's avatar:

```csharp
var avatar = Runner.Spawn(PlayerPrefab, CameraRig.transform.position, CameraRig.transform.rotation);
Runner.SetPlayerObject(Runner.LocalPlayer, avatar);
```

Unlike ordinary `Instantiate`, `Runner.Spawn` creates a network object that other clients also receive. `SetPlayerObject` associates it with our `PlayerRef`, Fusion's player identifier, so later scripts can find our avatar. `Runner.Despawn` removes a network object.

Inside [FusionNetworkPlayer.cs](Assets/Fusion%20and%20Essential%20Spawned%20Player%20Stuffs/Scripts/FusionNetworkPlayer.cs), `FixedUpdateNetwork()` first checks `HasStateAuthority`. Only our own avatar should copy our headset and controller poses. Its torso and ground contact follow with offsets, and grip/trigger inputs drive its hand animations. The networking components then carry those updates to our friend.

Movement tells us where someone is; torso color helps us recognize who they are:

```csharp
[Networked, OnChangedRender(nameof(ApplyPlayerColor))]
public Color PlayerColor { get; set; }
```

The authority assigns a color and Fusion shares the property. `OnChangedRender` names the method that applies a changed value to the visible material; `nameof` supplies that method's name. We also call `ApplyPlayerColor()` in `Spawned()`, because the change callback does not handle the initial spawn.

The lobby assigns separate starting slots, spaced **0.7 metres** apart, so avatars do not appear on top of one another. During a scene change, the spawner remembers our slot, removes the old avatar, and creates another using the new scene's rig. The same player identifier produces the same torso color. This preserves recognizable players and starting arrangements across scenes; it does not carry over the last position someone walked to.

These scripts use `using Fusion;` to access Fusion types. Keep `Assembly-CSharp` in Network Project Config's assemblies-to-weave list: Fusion's **weaver** generates the synchronization code behind `[Networked]` and, later, `[Rpc]`.

## 4. Let those players talk

We can now see our friend, but movement updates do not carry speech. Photon Voice handles that separately while following the Fusion session.

On the manager, use `FusionVoiceClient`, `Recorder`, `VoiceLogger`, and `FusionVoiceSetup`. Configure them as follows:

1. Set Recorder to **Microphone** and enable **Transmit Enabled**.
2. Enable **Use Fusion App Settings** and **Use Primary Recorder** on FusionVoiceClient; drag the Recorder into **Primary Recorder**.
3. Assign **Speaker Prefab** to an object with `Speaker` and `AudioSource`. The supplied scene uses the Speaker child of the avatar prefab as its template.

The Recorder captures our speech; the Speaker plays speech received from another player. `FusionVoiceSetup` handles microphone permission and recording readiness. This example provides room-wide voice; see [Photon's Voice setup](https://doc.photonengine.com/voice/v2/getting-started/voice-for-fusion) for further configuration.

## 5. Share something both players can grab

Once the countdown takes us into Game, the orange cube introduces a new problem. An avatar always belongs to its player, but either player should be able to move this cube. Its state authority therefore needs to change hands.

![The grabbable and interactable cubes](docs/images/cube-lessons.png)

On **Grabbable Cube**, use a collider, `Rigidbody`, `XRGrabInteractable`, `XRGeneralGrabTransformer`, `NetworkObject`, `NetworkTransform`, and [XRGrabNetworkInteractable.cs](Assets/Scripts/XRGrabNetworkInteractable.cs). Enable **Allow State Authority Override** so another player can take control, and disable **Destroy When State Authority Leaves** so the shared prop survives their departure.

XRI raises `selectEntered` when a hand grabs the cube. If we do not already control it, the script requests authority:

```csharp
if (HasStateAuthority) return;
awaitingAuthority = true;
Object.RequestStateAuthority();
```

Once authority arrives, the position XRI produces can be shared through `NetworkTransform`. On `selectExited`, the script releases authority after the last local hand lets go.

A request is not an immediate grant. `StateAuthorityChanged` handles a grant arriving after a quick release. There is only one state authority at a time, not shared control by everyone. Practice with one grabber at a time; simultaneous grabs and networked throwing require more handling than this example provides.

## 6. Make an interaction visible to everyone

The yellow-green cube uses the torso color we assigned earlier. When we hover over it, everyone should see it match our color and hide its prompt. Only we should see the hover particles.

Here we introduce an **RPC**, or **remote procedure call**: a request for selected clients to execute a method. Our avatar's networked property shares a current value; this RPC sends a color-changing action to the players currently present.

On **Interactable Cube**, use a collider, `XRSimpleInteractable`, `NetworkObject`, and [XRSimpleNetworkInteractable.cs](Assets/Scripts/XRSimpleNetworkInteractable.cs). Assign the renderer, text prompt, and particle system, and keep the object alive when its authority leaves.

In `hoverEntered`, the script finds our avatar with `Runner.GetPlayerObject(Runner.LocalPlayer)`, reads its `PlayerColor`, and passes that color to:

```csharp
[Rpc(RpcSources.All, RpcTargets.All)]
private void RpcSetColor(Color color)
{
    if (targetRenderer) targetRenderer.material.color = color;
    if (txtInfo) txtInfo.text = color.Equals(_originalColor) ? "Tap the cube!" : "";
}
```

`RpcSources.All` allows any peer with this object to send the call; `RpcTargets.All` executes it on current peers, including the sender. No authority transfer is needed for this RPC. Fusion recognizes RPC methods by the attribute and an `Rpc` prefix or suffix in their name.

The last local `hoverExited` sends the original color back, restoring the prompt too. Hover may come from a nearby hand or a ray. Particle playback stays in the local hover handlers, outside the RPC, so our friend sees the color change without seeing our particles.

This also shows the limit of an RPC: it has no persistent history for someone joining later. If a changed color must survive for future arrivals, store it in a networked property. Here, overlapping hovers simply use the last received event.

## 7. Complete the journey

Finally, our friend leaves. `PlayerLeft` in the spawner checks whether only one player remains in Game. The scene authority returns that player to Lobby, where `LobbyReady` reopens admission. A small readiness check covers departures during loading or a master change. The remaining player can now wait for someone else, completing the same loop we began with.

Try that whole journey with two clients and working VR input: **join → wait → enter Game → talk → grab → hover → leave**. Use matching App IDs, region, app version, session name, scene indices, timer settings, and spacing. Inspector settings are local configuration; the countdown and slots are shared state. If players wait alone, compare connection settings. For silent voice, check the Voice ID, microphone permission, Recorder, and Speaker assignments.

Read the linked scripts in this order alongside Photon's [Shared Mode Basics](https://doc.photonengine.com/fusion/v2/tutorials/shared-mode-basics/2-scene-and-player) to follow the same journey in code.

Screenshots are offline Unity scene previews using the saved materials. See [image sources](docs/images/README.md) and the [interactable-cube close-up](docs/images/interactable-cube.png).
