# Miru – Blender / Unity prototype

Adult goth streetwear NPC with red asymmetrical hair, bomber jacket and baggy cargo trousers, platform sneakers, silver piercings, choker, hip chain and a joint attached to the right middle finger.

## Place in Unity
Drag Assets/_Game/Characters/Miru/Miru.prefab into your scene. Unity metres, approximately 1.76 m tall, ground-level pivot, forward +Z. The prefab contains persistent DriftSkate/Toon materials, a capsule collider and a Humanoid Animator with a looping four-second Idle. No scenes are changed. This is a decorative character prefab, not a configured playable/network player.

## Edit / rebuild
Editable source: Art/Characters/Miru/Miru.blend. Mesh parts are joined into one weighted mesh, with material slots and 58 bones retained. Edit mesh geometry in Edit Mode and pose the skeleton in Pose Mode. Idle is retained as an NLA track.

Generator: Tools/Blender/build_miru.py with character-specific shapes in Tools/Blender/miru_shapes.py. Uses the shared helper definitions from Tools/Blender/build_stoners.py and Blender 5.2.2 LTS. No additional packages.

Run from the project root:
blender --background --factory-startup --python Tools/Blender/build_miru.py

This overwrites Miru's Blender source, FBX and Blender previews. Preserve manual edits before regeneration. FBX: Assets/_Game/Resources/Characters/NPC_Miru.fbx. Import settings are provided by the existing CharacterImport/NpcImport pipeline. Unity menu: DriftSkate > Miru > Prefab und Materialien aktualisieren. This rebuilds the generated prefab (preserving its asset GUID), updates the generated materials, keeps the animation controller and renders new previews. Back up manual prefab/material changes before running it. For manual Blender exports, select the armature and mesh, FBX forward -Z / up Y, no leaf bones, and export NLA animation tracks.

## Verification and limits
Blender reports 58 bones, 50,473 vertices, 100,416 triangles and no unweighted vertices. Unity validated a valid Humanoid avatar, Idle clip, skin bone references and prefab collider, then rendered the prefab using the game's Toon shader. See validation.json, Miru_preview.png, Miru_face.png, Miru_Unity.png and Miru_Unity_face.png. Logs: Logs/Miru_Blender.log and Logs/Miru_Unity.log.

Included animations: Idle and Sitting (both four-second loops). No walk, skate, dedicated sit-down/stand-up or smoke animation, gameplay, dialogue or smoke particles. Joint is a visual accessory with an exposed JointTip bone. Mesh/material count is a detailed prototype; there are no LODs, and no gameplay performance benchmark has been run.

Skatepark assets from the earlier test remain in Assets/_Game/SkateparkTest/Prefabs (KickerRamp, GrindRail, SkateLedge), with meshes, materials and colliders.

## Revision 2
Rebuilt face, flattened swept hair locks, smooth shoulder transitions, bomber collar, cargo pockets and hip chain. Materials retain the game shader with narrower outlines and softer shadow transitions. Unity previews now use front lighting. The original source, generator and previews are backed up under Previous/.


## Sitting animation
Drag Assets/_Game/Characters/Miru/Miru_Sitting.prefab into a scene to start seated in Play Mode. Its origin is at ground level; place a seat surface at about 0.45 m above the root. The preview seat is a visual guide only and is not part of the prefab. Miru_Sitting.controller starts in Sitting. The original Miru.controller supports the boolean IsSitting (true = Sitting, false = Idle), with 0.35-second blends. These are blends, not dedicated sit-down/stand-up actions. The standing prefab retains its standing collision capsule when switching; the seated prefab has a shorter capsule. The existing ParkMiru/StonerNpc runtime uses its own PlayableGraph and has not been switched to sitting by this asset change.

Sitting uses a relaxed breathing/head loop, left hand on the thigh and the right hand holding the joint clear. Unity verification samples the loop seam and foot stability. Blender source retains both NLA tracks; unmute Sitting and mute Idle to preview it.

