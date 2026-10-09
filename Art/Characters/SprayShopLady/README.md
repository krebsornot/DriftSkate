# Spray shop lady

Editable Blender character inspired by SprayShopLady_concept.png, adapted to DriftSkate's existing toon character pipeline. Long white sculpted hair, navy blazer and trousers, ivory top, magenta lining, silver hoops, sneakers and a spray can. This is a stylized interpretation of the illustration, not a pixel-identical reconstruction.

- Source: SprayShopLady.blend (metres, Z up, forward -Y).
- Generator: Tools/Blender/build_spray_lady.py, using existing shared character helpers.
- Model: Assets/_Game/Resources/Characters/NPC_SprayLady.fbx.
- Prefab: Assets/_Game/Characters/SprayShopLady/SprayShopLady.prefab.
- Placement: City.unity, child of CAN CLUB - Spray Shop, behind the counter.
- Rig: 57 bones, Humanoid; weighted mesh, looping four-second Idle.
- Behaviour: watches nearby skating/walking players in front of the counter; nods on greeting and purchase. Existing shop prices, input and inventory logic are preserved.

Rebuild with Blender background mode and --python Tools/Blender/build_spray_lady.py, then Unity menu DriftSkate > Spray Shop > Shop-Dame einbauen. Both commands regenerate this character's generated assets; preserve manual changes first. The Unity command updates only this NPC in the existing City scene.

Visual checks: SprayShopLady_blender.png, SprayShopLady_face.png, SprayShopLady_Unity.png, SprayShopLady_in_shop.png. Geometry counts are recorded in validation.json. The Unity import and placement check is recorded in unity-validation.txt; the shop play test also verifies the NPC and purchase flow.

Hair follows the head; there is no cloth simulation, locomotion animation or LOD mesh. Blender previews use workbench lighting; Unity previews use the game's toon shader.
