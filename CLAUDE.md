# DRIFT × SKATE: Hinweise für Claude

Online-Spiel (Unity 6000.3.2f1, URP, Netcode for GameObjects) im Jet-Set-Radio-Look: Drift-Auto fahren, jederzeit aufs Skateboard wechseln, Drifts und Tricks zählen in eine gemeinsame Combo. Spielerklärung und Steuerung stehen in der [README.md](README.md).

Zwei Leute arbeiten am Projekt (Fynn und ein Kumpel), beide teils mit Claude. Kommunikation auf **Deutsch**, locker mit „du“. Code-Kommentare und UI-Texte sind ebenfalls deutsch, und zwar ohne Umlaute (ae, oe, ue, ss), Bezeichner englisch.

## Zusammenarbeit
- Jeder arbeitet in einem eigenen Branch und öffnet einen PR auf `main`. **Unity-Szenen (`City.unity`, `Garage.unity`) lassen sich kaum mergen**, deshalb vorher absprechen, wer gerade an einer Szene arbeitet. Möglichst per Editor-Script (Menü `DriftSkate → …`) in die Szene bauen, dann kann man es nach einem Konflikt einfach neu laufen lassen.
- Auf demselben Rechner läuft manchmal parallel ein anderer KI-Agent (z. B. „Astra“). Tauchen fremde Dateien oder Compile-Fehler aus Code auf, den du nicht geschrieben hast: anhalten und nachfragen, nichts überschreiben.
- Beim Committen keine großen Dateien einchecken (GitHub-Limit 100 MB). `Library/`, `Builds/`, `Logs/`, Archive und `scratch_*.png` stehen in der `.gitignore`.
- Fremde, urheberrechtlich geschützte Modelle (z. B. aus anderen Spielen konvertierte Figuren) gehören **nie** ins Repo, sondern nur in den lokalen Mod-Ordner `%USERPROFILE%\AppData\LocalLow\DriftSkate Crew\DriftSkate\Mods\`. Der Projektordner `Mods/` wird mit jedem Build ausgeliefert.

## Aufbau
- `Assets/_Game/Scripts/`
  - `Core/`: Boot, Spielerprofil (`PlayerProfile.cs`, SaveSystem), Katalog, Eingabe (`GameInput.cs`), Toon-Materialien, Mods (`ModLibrary.cs`, GLB/glTF zur Laufzeit), **AutoTest**, PerfTest, Admin-Menü
  - `Vehicle/`: Drift-Physik (`VehicleController.cs`, eigene Raycast-Federung), Auto-Aufbau im Code (`CarBuilder.cs`, Livery/Folie, Underglow)
  - `Skater/`: `SkaterController.cs` (Zustände Riding/Air/Grinding/Bailed/Hidden/Walking/WallPlant/WallRide/Hitched), `SkaterRig.cs` (prozedurale Pose + IK), Board- und Figuren-Aufbau, Grind-Rails
  - `Gameplay/`: `PlayerAvatar.cs` (Spieler = Auto + Skater, Wechsel, Netzwerk-Sync), Combo, Kamera, NPCs und Quests, Graffiti, Engelsflügel, `Hitchable.cs` (Skitchen)
  - `World/`: Stadt-Generator (`CityBuilder.cs`, 5×5 Blöcke, Block 70 m, Straße 24 m), Himmel (`SkyLook.cs`), prozedurale Texturen, Wetter, Zeppelin, NPC-Verkehr (`Traffic.cs`, `TrafficCar.cs`)
  - `Editor/`: Asset-Pipelines, Szenen-Bau, Render-Tests (alles im Menü **DriftSkate**)
- `Art/`: Quellmodelle und Blender-Ergebnisse (OBJ/GLB/FBX), `Tools/Blender/`: Blender-Skripte für Skater, NPCs und Flügel
- Fast alles ist **prozedural**: Stadt, Autos, Boards, UI-Grafik (`UI/SprayArt.cs`) und Sounds (Motor, Regen, Spray) werden im Code erzeugt, es gibt keine Bild- oder Audiodateien dafür.

## Bauen und testen
Unity-Pfad: `C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe`. Batch-Aufrufe immer mit `-batchmode -projectPath . -quit -logFile Logs/<name>.log -executeMethod <Methode>`:
- Windows-Build: `DriftSkate.EditorTools.ProjectBuilder.BuildWindowsPlayer` → `Builds/DriftSkate/DriftSkate.exe` (anderer Zielordner: `-buildpath Builds/X/DriftSkate.exe`)
- Asset-Pipelines mit Kontrollbildern: `ZeppelinAssets.BatchAll`, `TrafficAssets.BatchAll`; Stadt-Bilder: `SimTests.RenderCityDetails` (`Logs/detail_*.png`)
- Spieltest (Exe): `DriftSkate.exe -autotest -testlog Logs/autotest.txt`. Er klickt durch Garage und Stadt und schreibt Prüfungen (`OK` / `FEHLT`) plus Screenshots nach `Logs/`. **Bekannte alte Fehler**, nicht von neuen Änderungen: „Bail-Out gestartet“, „kein Manual-Sturz …“, „Paket abgeliefert“.
- Online-Test: Host `-nettest-host -netport 7797`, nach ca. 12 s Client `-nettest-client 127.0.0.1 -netport 7797`, jeweils mit eigenem `-testlog`. Port 7797 nehmen, weil auf 7777 oft jemand selbst hostet.
- Sturz-Pose (Play-Mode im Batch, ohne `-quit`): `DriftSkate.EditorTools.BailPlayTest.Run` prueft, dass beim Hinfallen nichts im Boden steckt (`Logs/bail/*.png`)
- Performance: `DriftSkate.exe -perftest -perfweather klar|regen -perflog Logs/perf/x.txt`. **Nie nur den FPS-Zahlen trauen**, immer auch die Screenshots (`perf_*.png`) ansehen.

Vor Tests mit der Exe:
- prüfen, ob `DriftSkate.exe` schon läuft (es wird oft nebenher gespielt oder gehostet),
- `profile.json` im LocalLow-Ordner sichern und danach zurückspielen (Tests verändern Geld, Namen und Auswahl),
- wenn der Unity-Editor offen ist (`Temp/UnityLockfile`), im Batch-Modus mit einer Kopie des Projekts arbeiten.

Eingabe-Timing (Bail-Out, Landungen, Ollie) wie ein Mensch testen: mit Reaktionszeit (~0,3 s) und Tasten, die vom Fahren noch gehalten sind. Sofortige Bot-Eingaben haben echte Bugs schon übersehen.

## Stolperfallen
- **GPU Resident Drawer bleibt aus.** Mit dem Toon-Shader wird die Stadt sonst unsichtbar (vermeintlich 600 FPS bei leerem Bild).
- Das Spiel ist **CPU-gebunden** (Draw Calls). Kleine Teile ohne Schatten und Outline, Details auf Layer „Detail“ (ab 85 m ausgeblendet), Meshes zusammenfassen (siehe Paletten-Textur bei `TrafficAssets`).
- **Null-Normalen → NaN im Toon-Shader → Bloom färbt den ganzen Bildschirm weiß.** Doppelseitige Meshes nie durch doppelte Dreiecke mit `RecalculateNormals` bauen. Prüfung: `CarNormalCheck.Run`.
- Synchronisierte Enums (z. B. `SkaterState`, als Byte übertragen) nur **hinten** erweitern. Dasselbe gilt für Kodierungen wie `CarDesign.Encode`, damit alte Daten lesbar bleiben.
- NPC-Autos (`TrafficCar`) sind echte Rigidbodies (1050 kg, Hoehe und Kippen gesperrt), die ihrer Spur mit begrenzter Kraft folgen. Nie wieder kinematisch machen: dann wirken sie beim Rammen wie eine Wand.
- Online simuliert jeder Besitzer sein Auto und seinen Skater selbst. Zeppelin und NPC-Verkehr folgen der Server-Zeit (`NetworkManager.ServerTime`), es wird nichts übertragen.
- Skater-Bodenführung (`StepRiding`): das Hochdrücken bei Eindringen gehört nicht in `_lastSlopeVy`, sonst hüpft der Skater.
- Blender (bei Fynn über Steam, nicht im PATH): Unity-Humanoid braucht eine dreiteilige Wirbelsäule (Spine/Spine1/Spine2). FBX in der Ruhepose exportieren. Hand-Posen in Unity prüfen (`DriftSkate → Tests → NPCs pruefen und rendern`).
- Modelle aus three.js (OBJ/GLB) sind rechtshändig. Beim eigenen Einlesen X spiegeln und den Dreiecks-Umlauf drehen. MTL-Farben sind linear (`.gamma` nutzen).

## Look
- Der Toon-Abendhimmel mit tiefer Sonne (`Shaders/Sky.shader`, Werte in `World/SkyLook.cs`) ist abgesegnet: **feinjustieren, nicht ersetzen**. Lila Schatten, Nebel in Horizontfarbe, erleuchtete Fenster.
- Alles nutzt den eigenen Shader `DriftSkate/Toon` (über `ToonMaterials`), nicht URP/Lit.
- Animationen ruhig und langsam (Zyklen von mehreren Sekunden). Regenbogen-Verlauf (Pink → Violett → Cyan) nur als Akzent. Bei unklaren Stilfragen 2–3 Varianten mit Vorschaubildern anbieten statt zu raten.
