# DRIFT × SKATE (Arbeitstitel)

Online-Spiel für den PC im Jet-Set-Radio-Look: mit dem Drift-Auto durch eine fiktive Stadt fahren, jederzeit aussteigen (auch per Bail-Out während der Fahrt) und auf dem Skateboard weiter. Drifts und Tricks zählen in **eine gemeinsame Combo**, Punkte sind Geld. Damit kauft man Autos, Boards, Outfits und Garagen.

Game-Design-Dokument (Planung): https://claude.ai/code/artifact/58e41e38-1480-4a29-b6c1-7b521ce4a3fd

---

## Schnellstart

**Spielen ohne Unity:** `Builds/DriftSkate/DriftSkate.exe` starten.

**In Unity öffnen:**
1. Unity Hub → *Hinzufügen* → *Projekt von Datenträger* → diesen Ordner wählen.
2. Unity-Version **6000.3.2f1** (Unity 6.3) verwenden.
3. Falls beim ersten Öffnen ein Dialog **„URP Material upgrade“** erscheint: bestätigen. Das betrifft nur Materialien der Projektvorlage.
4. Szene `Assets/_Game/Scenes/Garage.unity` öffnen → **Play**.

Die Szene `City.unity` kann man auch direkt starten. Dann läuft automatisch eine Solo-Runde.

---

## Steuerung

| Aktion | Tastatur | Gamepad |
| --- | --- | --- |
| **Auto:** Gas / Bremse | W / S | RT / LT |
| Lenken | A / D | linker Stick |
| Handbremse | Leertaste | A |
| Kupplung (Clutch-Kick) | Shift links | X |
| Schalten (bei Handschaltung) | E / Q | RB / LB |
| Aussteigen (im Stand) / Bail-Out (in Fahrt) | F | Y |
| Auto zurücksetzen | R | Select |
| **Board:** Fahren | W A S D | linker Stick |
| Ollie | Leertaste halten + loslassen | A |
| Flip-Trick (+ Richtung) | J | X |
| Grab (+ Richtung, halten) | K | RB |
| Grind (an Kanten halten) | L | B |
| Manual (halten) | Shift links | LB |
| Spins (nach einem 180 fährt man Fakie weiter) | in der Luft A / D | Stick |
| Revert (aus Fakie zurückdrehen) | K am Boden | RB am Boden |
| Einsteigen | F (nah am Auto) | Y |
| Am Auto dranhaengen (Skitchen, neben/hinter fremdem Auto) / loslassen | F | Y |
| Vom Auto abschleudern (Slingshot) | Leertaste halten + loslassen | A |
| Graffiti sprühen | T (an gelben Flächen) | Steuerkreuz hoch |
| Tüte rauchen (Chill, siehe Gassen-Crew) | G | Steuerkreuz links |
| Auto rufen | R (weit weg) | Select |
| Umsehen | rechte Maustaste + Maus | rechter Stick |
| Kamera wechseln (Standard / Nah / Weit) | C | rechten Stick drücken |
| Nächster Song / Pause | M / Esc | Steuerkreuz rechts / Start |
| Admin-Menü (wenn Admin-Modus an) | F1 | – |

**Bail-Out:** Bei Tempo F/Y drücken, dann kurz vor der Landung bei **„JETZT!“** Leertaste/A drücken. Das gibt +800 und die Combo läuft weiter. Im Flug dreht man sich nicht (Lenken vom Auto stört also nicht), und ein einzelner etwas zu früher Druck ist noch in Ordnung (auch eine vom Driften noch gehaltene Leertaste zählt), nur wildes Drücken verpatzt die Landung. Noch gedrückte Auto-Tasten (Handbremse, Kupplung) lösen nach der Landung keinen Ollie oder Manual aus.

---

## Was drin ist

- **Drift-Physik wie CarX:** eigene Raycast-Federung, Reifen mit Schlupfkurve und Grip-Kreis, Heckantrieb mit Raddrehzahl, Clutch-Kick, Handbremse, Gegenlenk-Hilfe (4 Stufen), Automatik/Handschaltung.
- **Spielfigur:** stilisierter Skater aus Blender (Hoodie, Baggy-Pants, Sneaker, 4 Kopf-Varianten) mit Skelett. Animiert per Code: Füße per IK auf dem Deck, Knie federn, Push mit dem hinteren Fuß, Kurvenlage, Arme balancieren, Hände greifen bei Grabs, beim Flip lösen sich die Füße vom Board.
- **Skaten:** Schwung statt festem Tempo (einzelne Pushes, langes Ausrollen, bergab schneller), Carven mit Trägheit, Fußbremse, abgefederte Landungen. Ollie, 5 Flip-Tricks, 5 Grabs, Spins, Fakie fahren (nach 180 oder rückwärts von der Rampe), Revert, Grinds (50-50, Nosegrind, 5-0, Boardslide) mit Balance, Manuals, Stürze.
- **Stoner-NPCs:** drei detaillierte Figuren mit humanoidem Skelett (Finger, Kiefer, Augen, Lider) und eigenen Animationen auf einem Sofa am Platz, siehe unten.
- **Gassen-Crew:** Nix (Ticker) und Moe (vergibt Jobs) mit Kapuze auf und Baggy-Klamotten in einer Seitengasse am Brunnenplatz, siehe unten.
- **NPC-Verkehr:** 8 Toon-Kleinwagen in 6 Farben fahren Runden auf der rechten Spur, bremsen vor Kurven, Autos und Skatern, blinken vor dem Abbiegen. Online richtet sich der Verkehr nach der Server-Zeit (alle sehen dasselbe).
- **Skitchen:** Auf dem Board neben oder hinter einem fremden Auto (andere Spieler oder NPC) F/Y druecken: festhalten und mitfahren (Punkte nach Tempo, Combo bleibt offen). F/Y laesst los, Ollie laden + loslassen schleudert ab (SLINGSHOT). Wird online synchronisiert.
- **Wechsel Auto ↔ Board** mit durchgehender Combo: Aussteigen, Bail-Out mit Timing, Einsteigen (aus Tricks heraus „HOP IN!“), Auto rufen.
- **Combo-System:** Multiplikator bis ×20, 2,2 s Zeitfenster, Wand/Dreher/Sturz = halbe Punkte, Drift-Zonen und Wandnähe-Bonus.
- **Stadt:** Insel aus 5×5 Blöcken mit Häusern, Plätzen, Brunnenplatz, 2 Skateparks, 2 Drift-Plätzen, Hafen und vielen Graffiti-Spots (gelbe Flächen an Wänden). Rundum Häuserzeilen, an den vier Hafen-Seiten Kaimauern mit grindbarem Geländer und Blick aufs Wasser, eine Hängebrücke und eine Skyline im Dunst.
- **Garage (Hauptmenü)** im Sprühdosen-Look: Reiter und Knöpfe als Farbstriche mit Nasen, Graffiti-Logo, Sticker-Icons, Tag mit dem Spielernamen; beim Reiterwechsel wird der neue Reiter mit Spray-Sound „aufgesprüht“ (Lautstärke unter OPTIONEN → Menü). Alles im Code erzeugt (`UI/SprayArt.cs`, `Audio/SpraySound.cs`), keine Bild- oder Tondateien. 6 Autos, 9 Tuning-Regler, Lack + Crew-Farbe, 5 Boards (echte Decks mit Concave und Kicktails, Trucks, Rollen; jedes mit eigener Form), 14 Outfit-Teile, 4 Garagen, Graffiti-Editor, Mods, Optionen mit Admin-Modus.
- **Mods:** eigene Charaktere und Boards als GLB/glTF, Knochen werden automatisch erkannt (auch Mixamo, Rigify, VRM).
- **Online:** Netcode for GameObjects. Hosten oder per IP beitreten, jeder simuliert sein Auto selbst. Aussehen, Modus, Lenkung, Rauch und Tags werden synchronisiert.
- **Look:** eigener Toon-Shader (Cel-Shading, Outlines, Rim-Licht), Cartoon-Rauch, Reifenspuren in Crew-Farbe, Post-Processing.
- **Audio:** synthetischer Motor- und Reifensound (ohne Audiodateien), Radio für eigene Musik.

---

## Online spielen

- **Hosten:** Garage → FAHREN → *ONLINE HOSTEN*. Die eigene IP steht im Menü und oben links im Spiel.
- **Beitreten:** IP eingeben → *BEITRETEN*.
- Im selben WLAN/LAN funktioniert das direkt. Beim ersten Hosten fragt Windows nach einer Firewall-Freigabe; die muss erlaubt werden.
- Übers Internet muss der Host **Port 7777 (UDP)** im Router freigeben. Eine einfachere Lösung (Unity Relay mit Beitritts-Code) steht auf der Liste der nächsten Schritte.

## Eigene Musik

MP3, OGG oder WAV hier ablegen (oder in der Garage unter OPTIONEN → *ORDNER ÖFFNEN*):
`%USERPROFILE%\AppData\LocalLow\DriftSkate Crew\DriftSkate\Music`

Im selben Ordner liegen auch Spielstand (`profile.json`) und das eigene Graffiti (`graffiti.png`).

## Eigene Charaktere und Boards (Mods)

Eigene Modelle legst du in den Ordner **`Mods`** neben der Exe (im Unity-Projekt: `Mods` im Projektordner). Ausgewählt werden sie in der Garage im Tab **MODS**; dort gibt es auch *ORDNER ÖFFNEN* und *NEU LADEN*.

```
Mods/
  Skaters/<Name>/figur.glb    (+ optional mod.json)
  Boards/<Name>/board.glb     (+ optional mod.json)
```

- **Format:** GLB oder glTF 2.0. In Blender: *Datei → Exportieren → glTF 2.0*, Format *GLB*, bei Charakteren *Skinning* an.
- **Charaktere** brauchen ein Skelett mit Hüfte, Wirbelsäule, Hals, Kopf, Ober-/Unterarmen, Händen, Ober-/Unterschenkeln und Füßen. Die Knochen werden an ihren Namen erkannt: eigene Namen (`Hips`, `UpperArm_L` …), **Mixamo** (`mixamorig:LeftArm` …), Blender/Rigify (`upper_arm.L` …), VRM (`J_Bip_L_UpperArm` …) und ähnliche. Größe und Blickrichtung werden automatisch angepasst, Texturen in den Toon-Look umgewandelt.
- **Boards:** beliebiges Mesh (Deck, Achsen, Rollen). Länge, Ausrichtung und Höhe werden automatisch angepasst. Ein reines Deck ohne Rollen bekommt automatisch die Standard-Trucks und -Rollen.
- **mod.json** (alles optional):
  ```json
  { "name": "Mein Skater", "author": "Fynn", "height": 1.75,
    "bones": ["Hips=pelvis", "Head=kopf"] }
  ```
  Für Boards zusätzlich `"speed"`, `"pop"`, `"balance"` (1.0 = normal). Mit `bones` ordnest du Knochen selbst zu, falls die Erkennung einen nicht findet.
- Klappt etwas nicht, steht der Grund im MODS-Tab (z. B. „Knochen nicht gefunden: Foot_L“).
- **Online** sehen andere Spieler deinen Mod nur, wenn sie ihn selbst im Mods-Ordner haben; sonst sehen sie die Standardfigur bzw. das Standard-Board.
- **Beispiele:** `Mods/Skaters/Beispiel-Mixamo` (Figur mit Mixamo-Skelett) und `Mods/Boards/Beispiel-Longboard` (mit Textur). Erzeugt von `Tools/Blender/build_example_mods.py`.
- **Modelle aus anderen Spielen** müssen erst in GLB umgewandelt werden. Fremde Figuren nur privat nutzen, nicht in Builds oder im Portfolio weitergeben. Deshalb gehören sie in den persönlichen Mods-Ordner im Spielstand-Verzeichnis (`%USERPROFILE%\AppData\LocalLow\DriftSkate Crew\DriftSkate\Mods\Skaters`), nicht in `Mods/` im Projekt (das wird beim Build neben die Exe kopiert).
- **Tony Hawk's American Wasteland** (PC/reTHAWed, `.skin.wpc` + `.tex.wpc`) lässt sich direkt umwandeln:
  ```
  python Tools/THAW/thaw_to_glb.py "<THAW>/data/mod/UserMods/Skaters/<Name>/<figur>.skin.wpc" figur.glb
  python Tools/THAW/thaw_to_glb.py "<THAW>/data/mod/UserMods/CAS/<Board>/<board>.skin.wpc" board.glb
  ```
  Der Konverter liest Mesh, Texturen (DXT1/DXT5) und Knochen-Gewichte. Er setzt das THAW-Skaterskelett ein und berechnet die Gelenke aus den Proportionen der Figur. Die schwarzen Umriss-Hüllen lässt er weg (die zeichnet der Toon-Shader selbst), und gespiegelte Körperteile werden nach außen gedreht. Bei sehr kleinen oder großen Figuren in `mod.json` die `height` anpassen (Bart: `1.2`). CAS-Boards erkennt er von selbst und exportiert sie ohne Skelett; die gehören nach `Mods\Boards`.

## Admin-Modus (zum Testen)

Einschalten in der Garage unter **OPTIONEN → ADMIN: AN**. Im Spiel öffnet **F1** (oder Pause → *ADMIN-MENÜ*) das Admin-Menü.

| Funktion | Wirkung |
| --- | --- |
| Alles gratis | Kaufen kostet nichts, Stellplatz-Grenze aufgehoben |
| +1.000.000 $ / +100.000 $ | Geld dazu |
| Alles freischalten | alle Autos, Boards, Outfits und Garagen besitzen |
| Keine Stürze | schief landen, Balance verlieren und verpatzte Bail-Outs zählen nicht |
| Combo bricht nie ab | Wand, Dreher und Sturz beenden die Combo nicht |
| Mond-Schwerkraft | 40 % Schwerkraft beim Skaten (hohe Sprünge) |
| Turbo | Auto mit 1,8-fachem Drehmoment, Board mit 1,8-fachem Push |
| Zeitlupe | Spiel auf 40 % Tempo (nur Solo) |
| Debug-Anzeige | FPS, Tempo, Gang, Drehzahl, Driftwinkel, Zustand, Position |
| HUD ausblenden | für Screenshots und Videos |
| Teleport | Start, Brunnenplatz, Skatepark, Drift-Platz, Hafen Ost, Plaza, Gasse (Nix & Moe) |

Die Schalter werden im Spielstand gespeichert und wirken nur für dich.

---

## Projektstruktur

```
Assets/_Game/
  Scenes/        Garage.unity (Menü), City.unity (Stadt)
  Prefabs/       NetworkPlayer.prefab (Auto + Skater + Netzwerk)
  Shaders/       Toon.shader (Cel-Shading + Outline), FX.shader (Rauch, Spuren)
  Scripts/
    Core/        Eingabe, Katalog, Spielstand, Farben, ModLibrary (Mods laden), Admin, Autotest
    Vehicle/     VehicleController (Drift-Physik), VehicleSetup (Tuning), CarBuilder, Effekte, Motorsound
    Skater/      SkaterController (Fahrgefühl, Tricks, Grinds), SkaterRig (IK-Animation), SkeletonMap (Knochen-Erkennung), SkaterBuilder (Figur + Board), BoardBuilder (Deck, Trucks, Rollen), GrindRail
    Gameplay/    PlayerAvatar (Wechsel + Netzwerk), ComboSystem, DriftScorer, DriftZone, TagSpot, Kamera
    World/       CityBuilder (Stadtgenerator), MeshFactory (Rampen, Quarterpipes)
    Garage/      GarageMenu, GarageEnvironment, GraffitiEditor, OrbitCamera
    Network/     CityBootstrap (Solo/Host/Client starten)
    UI/          UIFactory, HUD, SprayArt (Spray-Grafiken)
    Audio/       MusicPlayer, SpraySound
    Editor/      ProjectBuilder, SimTests, AutoPlay
  Resources/Characters/Skater.fbx   die Spielfigur (aus Blender exportiert)
  Generated/     automatisch erzeugte Materialien, Meshes, Texturen
Tools/Blender/build_skater.py       baut die Spielfigur in Blender
Tools/Blender/build_example_mods.py baut die Beispiel-Mods
Tools/THAW/                         wandelt THAW-Figuren und -Boards in GLB um (thaw_to_glb.py; thaw_pak.py listet THAW-Archive)
Mods/                               eigene Charaktere und Boards (wird beim Build neben die Exe kopiert)
```

Autos bestehen im Moment noch aus Grundformen. Echte Modelle kann man später unter `Visual` einsetzen. Boards baut `Scripts/Skater/BoardBuilder.cs` als echte Meshes; ihre Maße (Länge, Breite, Achsabstand, Kicks, Concave, Rollen, Trucks) stehen als `BoardShape` im Katalog.

## Spielfigur ändern

Die Figur entsteht per Skript in Blender (Proportionen, Kleidung, Gesicht, Kopf-Varianten). Nach Änderungen an `Tools/Blender/build_skater.py` neu exportieren:

```
blender --background --factory-startup --python Tools/Blender/build_skater.py -- Assets/_Game/Resources/Characters/Skater.fbx Logs/skater_preview.png
```

Wichtig für eigene Modelle: Knochen heißen `Hips, Spine, Chest, Neck, Head, UpperArm_L/R, LowerArm_L/R, Hand_L/R, UpperLeg_L/R, LowerLeg_L/R, Foot_L/R`. Materialnamen bestimmen die Outfit-Farben (`Skin, Jacket, JacketTrim, Accent, Pants, Shoes, Sole, HeadWear, HairDark, Eyes, EyeWhite`), Kopf-Varianten heißen `Head_Hair, Head_Cap, Head_Beanie, Head_Phones`.

## Stoner-NPCs

Auf einem der beiden Plätze direkt neben dem Brunnenplatz hängen drei NPCs in der Ecke beim Baum auf einem alten Sofa ab (Position: `HangoutPosition` in `StonerNpc.cs`):

- **Jojo**: langer Hippie mit Dreads, Beanie, Baja-Hoodie und Cargohose, raucht ab und zu (mit Rauch).
- **Kalle**: sitzt mit Bucket-Hat, runder Sonnenbrille und Chips auf dem Sofa und isst.
- **Luna**: pastellpinker Dutt, offenes Flanellhemd, Schlaghose, tanzt zur Musik.

Sie schauen dir langsam nach, wenn du in der Nähe bist, lachen über Tricks und Stürze und sagen ab und zu etwas (`Scripts/Gameplay/StonerNpc.cs`).

Die Modelle baut `Tools/Blender/build_stoners.py` (Blender 4.2+ bzw. 5.x):

```
blender --background --factory-startup --python Tools/Blender/build_stoners.py -- Assets/_Game/Resources/Characters Logs
```

Optional als drittes Argument nur eine Figur (`jojo`, `kalle`, `luna`, `nix`, `moe`). Mit Vorschau-Ordner entstehen Blender-Bilder (`npc_*_tpose/face/idle.png`).

**Skelett für Animationen:** T-Pose, in Unity als **Humanoid** importiert (`Scripts/Editor/NpcImport.cs`), damit passen auch Mixamo- und andere Humanoid-Animationen. Knochen: `Hips, Spine, Spine1, Spine2, Neck, Head, Jaw, Eye_L/R, Lid_L/R` (Oberlider, nicht Teil von Humanoid), `Shoulder, UpperArm, LowerArm, Hand, Thumb1-3, Index1-3, Middle1-3, Ring1-3, Pinky1-3, UpperLeg, LowerLeg, Foot, Toes` (je `_L/_R`), bei Jojo zusätzlich `JointTip` (Glut, für den Rauch). Eigene Animationen in Blender als NLA-Spur anlegen; jede Spur wird ein Clip mit ihrem Namen (`Idle`, `Laugh`, `Smoke`, `Snack`, `Vibe`). `StonerNpc.Play("Name")` spielt einen Clip ab.

Prüfen und rendern: Menü *DriftSkate → Tests → NPCs prüfen und rendern* (Protokoll `Logs/npc_test.txt`, Bilder `Logs/npc_*.png`).

## Gassen-Crew (Nix und Moe)

In der Seitengasse gleich hinter dem Brunnenplatz (die Lücke zwischen zwei Häusern, deren Einfahrt der Stadtmitte am nächsten liegt; `AlleyCrew.cs` sucht sie beim Start aus den Hauswänden) hängen zwei Typen mit Kapuze auf ab. Im Admin-Menü gibt es dafür den Teleport *Gasse (Nix & Moe)*.

- **Nix**: langer, dünner Ticker. Schwarzer Oversize-Hoodie mit Kapuze über einer Cap (neongrüner Schirm schaut raus), neongrüner Schlauchschal, Bauchtasche quer über der Brust, extrem weite Camo-Cargohose, klobige weiße Sneaker, Lippenpiercing. Lehnt mit einem Fuß an der Hauswand, Hände in der Känguru-Tasche, schaut ab und zu die Gasse rauf und runter (`Lookout`).
- **Moe**: der Chef der Gasse. Graue Kapuze unter einer orangen Daunenweste, Goldkette mit Medaillon, tief sitzende Baggy-Jeans mit Boxershorts drüber, Boots. Sitzt auf einem umgedrehten Bierkasten, Geldbündel in der Hand, zählt ab und zu nach (`Count`).

**Nix verkauft Tüten** (`NixDealer.cs`): ansprechen mit E / X, kaufen mit E. Eine Tüte kostet $1.000, nach dem ersten Job für Moe $600 (Freundschaftspreis). Beim Kauf holt er die Tüte raus und legt sie dir in die Hand (`Deal`). Höchstens 5 im Rucksack.

**Tüte rauchen** mit **G / Steuerkreuz links** (`Chill.cs`): 60 s gechillt (mehrere stapeln sich bis 3 min). Das Combo-Zeitfenster läuft langsamer ab (×1,7 so lang), die Balance bei Grinds und Manuals wackelt weniger. Dazu Rauchwolke, sattere, leicht wabernde Farben und eine grüne Vignette; rechts oben stehen Tüten und Restzeit.

**Moe vergibt Jobs** (`MoeQuests.cs`), wie bei Jojo mit Fortschritt links oben, Leuchtsäule und Markierung:

| Job | Aufgabe | Belohnung |
| --- | --- | --- |
| Erste Lieferung | Paket in 2:00 zum Hafen Ost | $2.000 + Tüte, danach Freundschaftspreis bei Nix |
| Grüße an Jojo | Paket in 1:30 zu Jojo aufs Sofa (Jojo bedankt sich) | $2.500 |
| Markier das Revier | 3 Tags sprühen (Spots oder freie Wände) | $3.000 + Tüte |
| Express | Paket in 1:15 zum Skatepark Süd | $3.500 |
| Ganz entspannt | gechillt eine Combo über $5.000 fahren (Moe gibt eine Tüte, falls du keine hast) | $4.000 |

Danach geht es von vorn los, mit kürzerer Zeit, höheren Zielen und mehr Geld.

Die Modelle (und den Bierkasten `NPC_Crate`) baut `Tools/Blender/build_stoners.py` mit, Aufruf wie oben (`nix`, `moe`). Wichtige Werte: `WALL_Y` (Abstand Nix ↔ Wand, in Unity `AlleyCrew.WallGap`) und `CRATE_H` (Sitzhöhe). Die Tüte in Nix' Hand baut `NixDealer.cs` zur Laufzeit.

## Fahrgefühl einstellen

Am Skater im Prefab `NetworkPlayer` (Komponente *Skater Controller*, Abschnitt *Fahrgefühl*): Dauer eines Pushes, Beschleunigung, maximales Push-Tempo, Ausrollen, Luftwiderstand und seitlicher Grip. Die Auto-Physik steckt in `Scripts/Vehicle/VehicleSetup.cs`.

## Unity-Menü „DriftSkate“

| Eintrag | Wirkung |
| --- | --- |
| Projekt einrichten | baut fehlende Teile (Layer, Materialien, Prefab, Szenen) |
| Alles neu bauen | erzeugt Garage, Stadt und Prefab neu (**überschreibt eigene Änderungen**) |
| Stadt neu generieren | erzeugt nur die City-Szene neu (**überschreibt**) |
| Windows-Build erstellen | baut `Builds/DriftSkate/DriftSkate.exe` |
| Tests → Drift-Physik simulieren | fährt 3 Autos mit/ohne Lenkhilfe, Protokoll in `Logs/drift_test.txt` |
| Tests → Screenshots rendern | Bilder in `Logs/` |

Wichtige Werte zum Feintuning: Autos und Preise in `Scripts/Core/Catalog.cs`, Fahrphysik in `Scripts/Vehicle/VehicleSetup.cs`, Tricks und Punkte in `Scripts/Skater/SkaterController.cs`.

## Automatische Tests

Kompletter Spieltest im Editor (Garage, Mods, Admin-Modus, Fahren, Drift, Bail-Out, Tricks, Grind, Graffiti, Ein-/Aussteigen):

```
Unity.exe -batchmode -projectPath "C:\Users\Fynn\Desktop\Driftskate" -executeMethod DriftSkate.EditorTools.AutoPlay.Run -autotest
```

Online-Test mit zwei Instanzen der Exe:

```
DriftSkate.exe -batchmode -nographics -nettest-host -testlog host.txt
DriftSkate.exe -batchmode -nographics -nettest-client 127.0.0.1 -testlog client.txt
```

Der Testmodus nutzt einen eigenen Spielstand; der normale Spielstand bleibt unberührt.

---

## Abweichungen vom Game-Design-Dokument

| Geplant | Umgesetzt | Grund |
| --- | --- | --- |
| Cinemachine | eigene Verfolgerkamera (`CameraRig`) | Drift-Schwenk direkt steuerbar, kein Zusatzpaket |
| Unity Splines für Grinds | eigene `GrindRail`-Linienzüge | einfacher, im Editor als Gizmo sichtbar |
| Shader Graph | handgeschriebene HLSL-Shader | Outline-Pass und Toon-Licht in einer Datei |
| Lobby + Relay | Verbindung per IP | läuft ohne Unity-Cloud-Konto; Relay kommt als nächster Schritt |

## Bekannte Grenzen und nächste Schritte

- Echte 3D-Modelle für Autos (aktuell Grundformen).
- Unity Relay/Lobby für Online ohne Portfreigabe (Beitritts-Code).
- Fremde Graffitis erscheinen online als Standard-Tag in Crew-Farbe (die eigene Grafik wird noch nicht übertragen).
- Pylonen auf den Drift-Plätzen werden nicht synchronisiert.
- Speedlines-Effekt und Combo-Sound fehlen noch.
- Preise und Punkte nach ersten Spielrunden ausbalancieren.
- Handbremsen-Einlenken könnte sich noch etwas direkter anfühlen.
