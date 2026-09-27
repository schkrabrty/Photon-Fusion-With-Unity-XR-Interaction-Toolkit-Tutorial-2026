# Fusion lobby and voice

Start from `Assets/Scenes/Lobby Scene.unity`. There is one persistent **Fusion Network Manager** for the connection, lobby messages and Photon Voice. It moves with the players into Game and back to Lobby. You do not need another manager or another Recorder in Game.

`FusionLobbyValidation` was an Editor-only setup check, not part of Photon Voice or multiplayer synchronization. It has been removed. The separate `FusionVoiceManager` script has also been removed; its work now belongs to `FusionNetworkManager`.

## Inspector settings

Select **Fusion Network Manager** in Lobby:

- **Session Name**: `Room 1`. Players must use the same room, Photon apps, region and app version.
- **Minimum Players**: `2`.
- **Max Players**: `2`; increase this to allow more partners.
- **Waiting Delay**: `30` seconds after the minimum count is reached.
- **Full Room Delay**: `5` seconds when the room fills. This only shortens the remaining countdown.
- **Game Scene Build Index**: `1`; Lobby is build index `0`.
- **Player Prefab**: the existing Network Player prefab.
- **Reconnect Delay**: `5` seconds before retrying a failed connection or a closed/full room.
- **Player Spawn Spacing**: `2` metres between neighbouring starting positions in Game.

Set room rules before joining. The room creator's rules are shared with everyone. With minimum and maximum both set to 2, the second player starts the five-second countdown immediately.

The required components are already attached: `NetworkRunner`, `NetworkEvents`, `NetworkSceneManagerDefault`, `FusionPlayerSpawnerAndLobbyController`, `FusionVoiceClient`, `Recorder` and `VoiceLogger`. Keep this configured scene object; the scripts use its saved component references and events.

Adding `FusionPlayerSpawnerAndLobbyController` to a scene GameObject automatically fills **Camera Rig** with an XR Origin from that same scene, including an inactive rig. It searches for the `XROrigin` component, so the GameObject's name can change. For an existing spawner, use its component menu → **Find XR Origin in Scene**. If there is no XR Origin yet, add one and use that menu again. Runtime scene changes still find the newly loaded scene's rig automatically.

The lobby canvas contains status, player count and countdown text. Joining is automatic. **Match Camera Height** keeps the canvas at the current XR camera's height plus **Canvas Height Offset**.

## Reading the scripts

Your scripts are in `Assets/Fusion and Essential Spawned Player Stuffs/Scripts`.

- **FusionNetworkManager**: owns connection, automatic joining/recovery, lobby UI, microphone permission and voice logging. The file is grouped into Manager lifetime, Connect/join/reconnect, Lobby canvas/messages, and Voice/microphone sections.
- **FusionLobbyState**: a small Fusion `NetworkBehaviour` that shares the room rules and countdown. Only the current Shared master changes them.
- **FusionPlayerSpawnerAndLobbyController**: uses the manager's runner, spawns the local avatar in Game, remembers the room rules and returns the last player to Lobby.
- **FusionNetworkPlayer**: a Fusion `NetworkBehaviour` on each avatar. It reads the owner's XR tracking and shares head/hand poses, hand animation and Neck color.

Start with `FusionNetworkManager.Awake`, which keeps one manager and gets its attached components. `Start` enables voice callbacks and requests microphone permission, then connects and joins the Fusion room. Permission requests do not hold up joining.

The state and player scripts stay as separate Fusion components because their `[Networked]` properties belong to network objects. They are not additional connection managers. Project code uses explicit conditions and comments, with no question-mark operators.

## What survives scene changes

The same manager, runner, Voice Client and Recorder stay alive through **Lobby → Game → Lobby**. When a new Lobby scene loads, its temporary manager passes the new canvas/camera references to the persistent manager and removes itself before connecting.

`FusionLobbyState` uses a synchronized `TickTimer`, so everyone displays the same deadline. Reaching the minimum starts Waiting Delay; reaching capacity shortens it to Full Room Delay when necessary. Falling below the minimum cancels it. When the timer expires, the room closes to new joins and Fusion loads Game for everyone.

Each scene has its own XR Origin and AudioListener. Each new local avatar binds to that scene's camera and controllers; remote avatars use the replicated poses. The player's ID and Neck color remain consistent while the room stays connected.

Locally, the Network Player's head, Neck and duplicate hand renderers are hidden. You see your XR Origin's hands and the avatar's Ground Contact. Other players see your complete network avatar, including the colored Neck and animated hands.

Game starting positions form a horizontal row centred on the Game scene's XR Origin, along its right direction. With two players and spacing `2`, they start one metre to either side of that centre. Move or rotate Game's XR Origin in Edit mode to position the row, and leave enough clear floor for it. Spacing is the distance between player centres, not the empty space between their meshes.

The master shares the spacing and assigns each player a position in Lobby. Each client remembers its position across loading, then moves its entire XR Origin once before spawning its avatar. Headset height and hand tracking are preserved. Positions are assigned again for each new round, so replacement players stay near the group even as their Photon IDs increase. Players can move normally after spawning; this setting only controls their starting separation.

The Network Player prefab has a required `NetworkTransform` on its root to share its starting position and rotation. Its existing child NetworkTransforms share the tracked head, hands, Neck and ground marker relative to that root.

If the master leaves, Fusion Shared mode selects another connected player. When only one player remains in Game, the new scene authority returns them to Lobby in the same room. The survivor sees “Waiting for others to join...”. Once Lobby is ready, the room reopens and the next partner starts the normal countdown.

A connection failure is different from changing scenes: a stopped runner cannot be reused. The manager reloads Lobby with fresh networking components and retries automatically.

## Photon Voice

Your Voice App ID is configured in Fusion's Photon App Settings. The attached `FusionVoiceClient` follows the Fusion room and region automatically. `FusionNetworkManager` starts the microphone only after permission is granted and the Voice room has been joined. A player who denies microphone access can still hear others. Returning from device settings rechecks permission.

The existing **Network Player → Speaker** child supplies the `Speaker` and `AudioSource` template. Photon creates a playback instance under the persistent manager for each remote voice, then removes it when that stream leaves. There is no separate Speaker prefab asset. This is 2D room chat, which also works in Lobby before avatars spawn.

Keep **Use Primary Recorder** enabled on the Voice Client. This setup uses the persistent Recorder directly; the avatar does not need a `VoiceNetworkObject`, Recorder or Voice Client.

Useful Recorder settings:

- **Transmit Enabled**: on; turn it off in Play mode to mute outgoing audio.
- **Voice Detection**: on, threshold `0.01`. Lower the threshold if quiet speech is cut off.
- **Microphone Type**: Photon, with Unity microphone fallback enabled.
- **Stop Recording When Paused**: on.
- **Debug Echo Mode**: off; enable temporarily for a solo microphone test.
- **Recording Enabled** and **Record When Joined**: saved off. The manager enables recording after checking permission and room membership.

Filter the Console for `[Fusion]` to see room membership, master changes, countdown, scene loading, avatar spawning, XR binding and colors. Filter for `[Voice]` to see permission, connection, recording and remote audio. Voice actor numbers are labelled separately because they can differ from Fusion player IDs.

## Try it with two players

1. Let Unity finish compiling and reopen Lobby if it was already open while its file changed.
2. Run two clients. The first waits at `1 / 2`; the second starts the five-second countdown and both enter Game.
3. Confirm players start apart by Player Spawn Spacing. Talk in Lobby and Game, and check remote head/hand tracking and Neck colors on the headsets.
4. Close the original master's app. After Photon detects the departure, the survivor should return to Lobby and keep their voice connection.
5. Join another partner and confirm voice and tracking work in the next round.

Physical microphone quality, permission prompts, headset output and pause/resume still need testing on the target devices. Completed isolated checks are recorded in `Fusion-Voice-Validation.txt`; no validation script or menu is needed in your project.

## Automatic local hand actions

Both Hand Presence prefabs have **Auto Assign Input Actions** enabled. `HandPresence` reads the detected controller's Left/Right characteristics, finds the parent XR Origin's `InputActionManager`, and uses the matching `XRI Left Interaction` or `XRI Right Interaction` map. It binds **Activate Value** to the hand's Trigger animation and **Select Value** to Grip, preserving gradual finger movement.

In both scenes, each Hand Presence is a child of its corresponding tracked controller with local position `(0, 0, 0)`, rotation `(0, 0, 0)` and scale `(1, 1, 1)`. Keep that parent transform aligned with the controller. The spawned hand model retains its small wrist-alignment offset and rotation from its prefab.

Before hardware connects, the existing Controller Characteristics setting identifies the side. You do not need to drag individual actions into each hand. The action fields are filled in Play mode and rebound after a controller reconnect; each scene's new rig binds its own actions. The existing Input Action Manager enables/disables the shared actions.

To use custom action maps, turn off **Auto Assign Input Actions** and fill the existing Activate Action and Select Action fields. Missing actions/models are handled without null-reference errors, and reconnecting does not create duplicate hand models.

Photon documentation: [Fusion Voice integration](https://doc.photonengine.com/voice/v2/getting-started/voice-for-fusion), [Recorder settings](https://doc.photonengine.com/voice/v2/getting-started/recorder), [Fusion scene loading](https://doc.photonengine.com/fusion/v2/manual/scene-loading).

## Cube interaction

The Game scene's **Interactable Cube** has `XRSimpleInteractable`, `NetworkObject` and `XRSimpleNetworkInteractable`. Touching or pointing at it shows the hovering player's avatar color on both clients. Its Mesh Renderer and first child TMP text are filled automatically when the script is added or validated in the Inspector, including inactive text children. Existing manual references are preserved; Reset fills missing references again. Its label resets when that player stops hovering. The most recent player to begin hovering controls the displayed color.

The **Grabbable Cube** has `XRGrabInteractable`, `XRGeneralGrabTransformer`, `Rigidbody`, `NetworkObject`, `NetworkTransform` and `XRGrabNetworkInteractable`. Move a hand near it and squeeze the controller's **grip** (the XRI Select action) to grab; release grip to drop. The trigger animates the index finger and supplies Activate, rather than Select. Both cubes and both Near-Far Interactors use the Default interaction layer.

Grabbing requests Fusion state authority and waits for that transfer before moving the object. A held cube rejects another player's grab. On release, the owner restores the Rigidbody to dynamic before XRI applies throw velocity, even if it was kinematic before ownership arrived. The last owner keeps authority so falling and throwing remain synchronized; the next player can take authority when grabbing it. The cubes are configured to survive their owner's departure. Start from Lobby for multiplayer; launching Game alone allows local interaction without a network connection.

The cubes and their children use the **Interactables** physics layer (layer 6). Both Near-Far Interactors include this layer in their **Sphere Interaction Caster → Physics Layer Mask** and **Curve Interaction Caster → Raycast Mask**. Their XRI **Interaction Layer Mask** still matches the cubes' **Default** interaction layer. These are separate filters: changing a cube's GameObject layer or the Physics collision matrix does not update an interactor's cast masks. See Unity's [interaction layer documentation](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.3/manual/interaction-layers.html).

The local XR Origin's Near-Far Interactors perform grabbing and exclude **PlayerHitbox**. Your disabled physics collision pair **Interactables–PlayerHitbox** is preserved. The source hand-model colliders under XR Origin are unchanged. Keep the cubes' colliders enabled: colliders alone do not make an object an XRI interactable.

## Forward movement and avatar collisions

The shared XR Rig prefab uses head-relative continuous movement with **Enable Fly** off and **Use Gravity** on. Forward joystick input is projected onto the ground plane even when the headset tilts upward. Both Lobby and Game inherit these settings.

The XR Origin's **CharacterController → Layer Overrides → Exclude Layers** now includes **PlayerHitbox**. This prevents solid network-avatar colliders from pushing or lifting the local rig. Disabling only **Interactables–PlayerHitbox** collisions did not protect the CharacterController, which is on Default. This exclusion also means the rig can pass through remote avatars; walls and floors retain their normal collisions. Invisible local network-avatar meshes still have active colliders.

Console messages prefixed `[Fusion][Grab]` show ownership requests, successful grabs and releases. `[Fusion][Touch]` shows hover changes.

## Teleportation

Both scenes use **Teleportation Area** on the Plane. Its **Interaction Layer Mask** is **Teleport**, matching the Teleport Interactors. This is the XRI interaction mask, separate from the GameObject's physics Layer, which remains Default. The grip-controlled Near-Far Interactors use the Default interaction mask for cubes and cannot select the floor.

Push the **right thumbstick forward** and hold to aim at the floor, then release to teleport to the hit point. **Grip** cancels aiming; outside teleport mode it grabs objects. The left thumbstick remains configured for continuous movement. To use left-hand teleport instead, disable **Smooth Motion Enabled** on Left Controller's Controller Input Action Manager.

The Teleport Interactor's XR Interactor Line Visual already references **Directional Teleport Reticle**. A separate Custom Reticle on the Plane is unnecessary. The normal reticle appears over a valid teleport target. Both planes previously used the Default interaction mask, which let grab rays select their centre while teleport rays could not select them.

Both Teleport Interactors also include the **Interactables** physics layer in their Raycast Mask so the cubes can obstruct the ray. Their XRI Interaction Layer Mask remains **Teleport**, so the cubes do not become teleport destinations.

## Avatar hitboxes for future bullets

The entire Network Player prefab now uses physics layer **PlayerHitbox** (layer 8), as assigned by you. Its head and Neck colliders are triggers; its enabled hand colliders and Ground Contact collider retain your non-trigger settings. They remain active even when local avatar renderers are hidden. Non-trigger colliders create solid collision shapes; they do not automatically add Rigidbody physics. There is currently no Rigidbody on the Network Player prefab.

The interaction-ray masks and CharacterController exclude PlayerHitbox. For physical bullets, include PlayerHitbox in the projectile's collision filtering and give the projectile a Rigidbody. Trigger bullets can use `OnTriggerEnter` with either type of target collider. Solid bullets need `OnCollisionEnter` for the solid hands and `OnTriggerEnter` for the head/Neck. For raycast bullets, include PlayerHitbox in the hit mask and use `QueryTriggerInteraction.Collide` to include trigger hitboxes. Retrieve the target avatar from the hit collider's parent `FusionNetworkPlayer`; ignore the shooter's own avatar and explicitly exclude Ground Contact from damage, since it now shares the hitbox layer. Unity documents the [trigger event requirements](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Collider.OnTriggerEnter.html) and [raycast trigger filtering](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QueryTriggerInteraction.html).

Bullet firing, health and scoring scripts are not present yet. When implemented, route a validated hit to the appropriate Fusion state authority, update networked health and score once per shot, and prevent duplicate damage when a shot overlaps multiple body colliders or is observed by multiple clients. Unity collider callbacks alone do not synchronize damage or award points.
