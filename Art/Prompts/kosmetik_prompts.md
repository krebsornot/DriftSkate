# Kosmetik neu modellieren: Prompts fuer Claude 3D Design

Jeder Block unten ist ein kompletter Prompt (Stil + Passform + Teil): einfach den ganzen Block kopieren.

Stand: 14 Outfit-Teile aus `Core/Catalog.cs` (Boards bleiben wie sie sind).

## Was man vorher wissen muss

- **Kopf-Teile** (Haare, Cap, Beanie, Kopfhoerer) sind heute eigene Meshes in `Resources/Characters/Skater`
  (Blender: `Tools/Blender/build_skater.py`, Varianten `Head_Hair`, `Head_Cap`, `Head_Beanie`, `Head_Phones`).
  Sie haengen starr am Knochen `Head`, lassen sich also 1:1 tauschen.
- **Jacke, Hose, Schuhe** sind heute **ein und dasselbe Mesh**, nur umgefaerbt (Hoodie Orange = Jacke Blau in Orange).
  Mit neuen Modellen bekommt jedes Teil eine eigene Form. Dafuer muessen sie in Blender auf das Skater-Skelett
  gewichtet werden (Rest-Pose siehe unten) und `SkaterBuilder` muss pro Slot das passende Mesh einschalten,
  so wie jetzt schon bei den Kopf-Varianten.
- Die Farbe kommt im Spiel aus dem Katalog ueber die **Material-Namen** (`HeadWear`, `Jacket`, `JacketTrim`, `Pants`,
  `Shoes`, `Sole`, `Accent` = Crew-Farbe, `HairDark`). Darum stehen in jedem Prompt die Material-Slots dabei.
  Keine Texturen noetig, der Toon-Shader macht den Look.

### Masse der Standardfigur (Meter, Blender: Z oben, Blick nach -Y)
| Stelle | Wert |
|---|---|
| Koerpergroesse | 1,80 (Scheitel 1,82) |
| Kopf | Mitte ca. z 1,68, ca. 0,24 breit, 0,27 tief |
| Hals | z 1,44 bis 1,56 |
| Schultern | x +-0,20, z 1,415 |
| Ellbogen / Handgelenk | x +-0,29 z 1,15 / x +-0,355 z 0,935 (A-Pose, Arme haengen leicht abgespreizt) |
| Huefte | x +-0,105, z 0,88 |
| Knoechel | x +-0,115, z 0,115 |
| Fuss | Spitze y -0,17, Sohle z 0, Laenge ca. 0,26 |

---

## Kopf (Slot Head, starr am Kopf-Knochen)

### 1. Struwwelhaare (`head_hair`, gratis)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Head-worn piece only (no head, no face). Fits a stylized head centered at (0, 0, 1.68), about 0.24 m wide,
0.27 m deep, crown at z 1.82. Origin at (0, 0, 1.56) (neck/head joint).

Messy spiky skater hair, short at the sides, big chunky spikes on top and toward the front like
anime-style streetwear hair, a few strands falling over the forehead but leaving the eyes free.
Material slot "HeadWear" for all hair (recolored in game, default very dark brown #2A1E1A).
```

### 2. Cap Gelb (`head_cap_y`, 8.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Head-worn piece only (no head, no face). Fits a stylized head centered at (0, 0, 1.68), about 0.24 m wide,
0.27 m deep, crown at z 1.82. Origin at (0, 0, 1.56) (neck/head joint).

Baseball cap worn slightly tilted backwards to one side, rounded six-panel crown with a top button,
pre-curved brim, adjustable strap gap at the back with a small buckle. Short dark hair visible under the cap
at the sides and neck. Material slots: "HeadWear" (cap, game color yellow), "Accent" (button, buckle and a
thin stitched stripe around the crown, crew color), "HairDark" (hair under the cap).
```

### 3. Beanie Pink (`head_beanie_p`, 12.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Head-worn piece only (no head, no face). Fits a stylized head centered at (0, 0, 1.68), about 0.24 m wide,
0.27 m deep, crown at z 1.82. Origin at (0, 0, 1.56) (neck/head joint).

Slouchy knit beanie with a thick folded cuff and an oversized fluffy pompom on top, knit ribs modeled as
shallow vertical grooves in the cuff. Sits low over the ears, a few strands of hair poke out over the forehead.
Material slots: "HeadWear" (beanie and pompom, game color pink), "Accent" (small rectangular patch on the cuff,
crew color), "HairDark" (hair strands).
```

### 4. Kopfhoerer (`head_phones`, 30.000) – eingebaut (DJ-Version)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Head-worn piece only (no head, no face). Fits a stylized head centered at (0, 0, 1.68), about 0.24 m wide,
0.27 m deep, crown at z 1.82. Origin at (0, 0, 1.56) (neck/head joint).

Oversized futuristic street DJ headphones over short messy spiky dark hair. Thick padded headband with a
split double-bar design and a small raised fin on top, chunky angular ear cups (rounded hexagon shape)
with a glowing ring around the outside of each cup, a short antenna-like stub on the left cup, small
mechanical hinge pieces with visible screws where the band meets the cups, a coiled cable from the left
cup hanging down to about collarbone height and ending in a chunky plug. The headphones sit slightly
tilted, as if pushed back a bit on the head.
Material slots: "HeadWear" (headband and cup shells, recolored in game, default cyan),
"Accent" (glowing cup rings, fin stripe and plug tip, crew color), "Ink" (ear pads, hinges, cable,
near black), "HairDark" (hair).
```

### 4b. Bucket Hat (`head_bucket`, 20.000, neu) – eingebaut
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Head-worn piece only (no head, no face). Fits a stylized head centered at (0, 0, 1.68), about 0.24 m wide,
0.27 m deep, crown at z 1.82. Origin at (0, 0, 1.56) (neck/head joint).

Slouchy 90s streetwear bucket hat, worn low so the brim shades the eyes a little but the eyes stay
visible. Soft round crown with a slight dent on top, wide downward-sloping floppy brim with a thick
rolled edge and visible stitched rings modeled as shallow grooves, two small metal air vents on the
sides, a thin cord hanging loosely under the chin with a chunky toggle stopper. A few strands of messy
dark hair poke out at the sides and the back of the neck.
Material slots: "HeadWear" (crown and brim, recolored in game), "Accent" (stitched band around the crown
and the cord toggle, crew color), "Ink" (air vents and cord, near black), "HairDark" (hair).
```

---

## Jacke (Slot Jacket, gewichtet auf Chest/Spine/Arme)

### 5. Jacke Blau (`jacket_blue`, gratis)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Upper-body garment only (no body inside, open at neck, wrists and waist), shaped to fit over a slim stylized
torso: shoulders at x +-0.20 z 1.415, elbows at x +-0.29 z 1.15, wrists at x +-0.355 z 0.935, waist at z 0.95.
Sleeves follow the A-pose arms. Enough edge loops at shoulders and elbows to bend cleanly when rigged.

Classic zip-up windbreaker jacket, slightly oversized, hip length, high stand-up collar, front zipper,
elastic cuffs and waistband, two diagonal chest pockets. Material slots: "Jacket" (main, game color blue),
"JacketTrim" (collar, cuffs, waistband), "Accent" (zipper pull and a stripe across the chest, crew color).
```

### 6. Hoodie Orange (`jacket_orange`, 10.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Upper-body garment only (no body inside, open at neck, wrists and waist), shaped to fit over a slim stylized
torso: shoulders at x +-0.20 z 1.415, elbows at x +-0.29 z 1.15, wrists at x +-0.355 z 0.935, waist at z 0.95.
Sleeves follow the A-pose arms. Enough edge loops at shoulders and elbows to bend cleanly when rigged.

Baggy pullover hoodie, hood down and bunched at the back of the neck, kangaroo front pocket, thick ribbed
cuffs and hem, drawstrings with chunky tips hanging over the chest, sleeves slightly too long.
Material slots: "Jacket" (main, game color orange), "JacketTrim" (hood lining, cuffs, hem),
"Accent" (drawstring tips and a simple geometric chest print, crew color).
```

### 7. Track Jacket Lime (`jacket_lime`, 18.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Upper-body garment only (no body inside, open at neck, wrists and waist), shaped to fit over a slim stylized
torso: shoulders at x +-0.20 z 1.415, elbows at x +-0.29 z 1.15, wrists at x +-0.355 z 0.935, waist at z 0.95.
Sleeves follow the A-pose arms. Enough edge loops at shoulders and elbows to bend cleanly when rigged.

Retro 90s track jacket, fitted at the waist, zipped halfway, tall collar, two contrasting stripes running
down each sleeve from shoulder to cuff, piping along the shoulders, zipper pockets at the hips.
Material slots: "Jacket" (main, game color lime), "JacketTrim" (collar, cuffs, hem), "Accent" (sleeve stripes
and piping, crew color).
```

### 8. Bomber Weiss (`jacket_white`, 25.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Upper-body garment only (no body inside, open at neck, wrists and waist), shaped to fit over a slim stylized
torso: shoulders at x +-0.20 z 1.415, elbows at x +-0.29 z 1.15, wrists at x +-0.355 z 0.935, waist at z 0.95.
Sleeves follow the A-pose arms. Enough edge loops at shoulders and elbows to bend cleanly when rigged.

Puffy MA-1 style bomber jacket, quilted volume, ribbed knit collar, cuffs and waistband, utility zip pocket on
the left sleeve, embroidered-looking oversized patch on the back (simple star or wing shape, modeled as a raised
flat shape). Material slots: "Jacket" (shell, game color white), "JacketTrim" (rib knit parts),
"Accent" (back patch and sleeve pocket zipper, crew color).
```

---

## Hose (Slot Pants, gewichtet auf Hips/Beine)

### 9. Jeans (`pants_denim`, gratis)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Trousers only (no body inside, open at waist and ankles), waistband at z 0.98, hips at x +-0.105 z 0.88,
knees halfway down, hems end around z 0.10 just over the shoes (ankles at x +-0.115 z 0.115).
Enough edge loops at hips and knees to bend cleanly when rigged.

Relaxed straight-leg skater jeans, slightly baggy, cuffed once at the ankle, visible belt with a simple
buckle, back pockets, light wear patch on one knee modeled as a shallow raised shape.
Material slots: "Pants" (denim, game color #2F3E6E), "Accent" (belt buckle and back pocket stitching, crew color),
"Sole" (belt, off-white).
```

### 10. Cargo Oliv (`pants_cargo`, 9.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Trousers only (no body inside, open at waist and ankles), waistband at z 0.98, hips at x +-0.105 z 0.88,
knees halfway down, hems end around z 0.10 just over the shoes (ankles at x +-0.115 z 0.115).
Enough edge loops at hips and knees to bend cleanly when rigged.

Wide cargo pants with big bellows side pockets on both thighs, flap pockets with buttons, adjustable straps
at the ankles that bunch the hem, a short strap dangling from one pocket.
Material slots: "Pants" (game color olive #5B6B3A), "Accent" (buttons and strap buckles, crew color).
```

### 11. Baggy Lila (`pants_purple`, 15.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

Trousers only (no body inside, open at waist and ankles), waistband at z 0.98, hips at x +-0.105 z 0.88,
knees halfway down, hems end around z 0.10 just over the shoes (ankles at x +-0.115 z 0.115).
Enough edge loops at hips and knees to bend cleanly when rigged.

Extremely baggy 2000s skate pants, very wide legs pooling over the shoes, low-slung waist with exposed
boxer waistband, a wallet chain looping from the front belt loop to the back pocket.
Material slots: "Pants" (game color purple), "JacketTrim" (boxer waistband), "Accent" (wallet chain, crew color).
```

---

## Schuhe (Slot Shoes, links und rechts, gewichtet auf Foot/LowerLeg)

### 12. Sneaker Weiss (`shoes_white`, gratis)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

A pair of shoes (left and right as separate parts), feet at x +-0.115, sole on z 0, toe pointing -Y,
length about 0.26 m (stylized, slightly oversized), ankle opening around z 0.12.

Clean low-top court sneaker, rounded toe, simple side panel, flat laces, padded collar.
Material slots: "Shoes" (upper, game color white), "Sole" (thick sole, off-white),
"Accent" (heel tab and a simple straight side stripe, crew color).
```

### 13. Sneaker Rot (`shoes_red`, 7.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

A pair of shoes (left and right as separate parts), feet at x +-0.115, sole on z 0, toe pointing -Y,
length about 0.26 m (stylized, slightly oversized), ankle opening around z 0.12.

High-top basketball sneaker with ankle strap, chunky padded collar, toe cap, visible stitching lines as
shallow grooves, big tongue sticking out above the laces.
Material slots: "Shoes" (upper, game color red), "Sole" (thick cupsole, white), "Accent" (ankle strap and
lace tips, crew color).
```

### 14. Skate-Schuh Gelb (`shoes_yellow`, 11.000)
```
Style: stylized cel-shaded street fashion in the spirit of early-2000s Dreamcast graffiti-skater games
(chunky, exaggerated, bold silhouettes), but original design, no logos or brand names.
Clean low-to-mid poly game asset (under 4k triangles), smooth shading, no textures, flat solid colors only,
every color on its own named material slot as listed. Real-world scale in meters, Z up, character faces -Y.
Fits a stylized 1.80 m tall skater character in A-pose (arms hanging down ~20 degrees outward).
Watertight where possible, no zero-area faces, no duplicated double-sided triangles.

A pair of shoes (left and right as separate parts), feet at x +-0.115, sole on z 0, toe pointing -Y,
length about 0.26 m (stylized, slightly oversized), ankle opening around z 0.12.

Puffy early-2000s skate shoe: very fat padded tongue, thick padded collar, wide flat vulcanized sole with a
toe bumper, ollie-protection panel on the side, fat laces.
Material slots: "Shoes" (upper, game color yellow), "Sole" (sole and toe bumper, white),
"Accent" (side panel and heel logo shape, crew color).
```

---

## Danach (Einbau)

**Alle Slots (fertig eingerichtet):**
1. GLB als `Art/Outfits/<katalog-id>.glb` ablegen (z. B. `jacket_lime.glb`, `pants_cargo.glb`).
2. Slot und Id in `OUTFIT_MODELS` in `Tools/Blender/build_skater.py` eintragen, z. B. `("Pants", "pants_cargo")`
   (Slots: `Head`, `Jacket`, `Pants`, `Shoes`). Ganz neue Teile hinten in `Catalog.Outfits` anhaengen.
3. Figur neu exportieren:
   `blender --background --factory-startup --python Tools/Blender/build_skater.py -- Assets/_Game/Resources/Characters/Skater.fbx Logs/skater_preview.png`
   (legt pro Teil auch Vorschaubilder von vorn und hinten an).
4. Pruefen: `DriftSkate → Tests → Screenshots rendern` (`Logs/shot_character_*.png`).

Das Skript gewichtet die Teile automatisch nach den naechsten Koerperpunkten. `SkaterBuilder` zeigt das Mesh
`<Slot>_<id>`, ohne eigenes Modell bleibt die alte Kleidung.

Schuhe haengen starr am Fuss-Knochen (linker Schuh bei +X). Material `JacketTrim` wird bei Schuhen zu `Ink`
(dunkle Sohle/Schnuersenkel statt Jackenfarbe), siehe `MATERIAL_REMAP`.

Kopf-Teile haengen starr am Kopf-Knochen. Ist das Modell um den Ursprung gebaut, hebt das Skript es um 1,56 m an.
