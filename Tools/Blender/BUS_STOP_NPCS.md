# Bewohner der Baustellen-Haltestelle

Bruno: Wollmuetze, grauer Bart, geflickte olivgruene Jacke, weite Jeans, Bierflasche.
Fiete: zerzauste Haare, braungraue Jacke, ausgebeulte Hose, kleine Pfeife mit Rauch-Animation.
Beide sitzen dauerhaft; Idle, Drink/Smoke und Laugh sind eigene Humanoid-Clips.

`Gameplay/BusStopResidents.cs` stellt beide beim Laden der City automatisch auf.
Voraussetzung: ein Objekt `Baustelle` unter `Block_04_B`. Fehlt die Baustelle,
wird nichts aufgestellt. Bank, Dach, Haltestellenschild, Fahrplan und Gepaeck
entstehen am noerdlichen Gehweg; Abstand relativ zur Baustelle `(6, 0.18, 33.1)`.
Ein erneuter Aufruf von `Spawn()` erzeugt keine Duplikate. Die City-Szene muss
nicht neu gespeichert oder generiert werden.

Modelle neu bauen (absolute Ausgabe-Pfade verwenden):

```powershell
& 'C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe' --background --factory-startup --python Tools/Blender/build_bus_stop_npcs.py -- 'C:/Users/Fynn/Desktop/Driftskate/Assets/_Game/Resources/Characters' 'C:/Users/Fynn/Desktop/Driftskate/Logs/bus_stop'
```

Das Script verwendet die Modell-, Gewichts-, Export- und Animationshelfer aus
`build_stoners.py`, ohne dessen bestehende Figuren neu zu exportieren.
Die Requisiten entstehen in Unity und folgen den rechten Haenden.
Alle Materialien nutzen den vorhandenen Toon-Shader.

Kontrollbilder und Rig-Pruefung: `DriftSkate > Tests > Haltestellen-NPCs pruefen und rendern`
oder Batch-Methode `DriftSkate.EditorTools.BusStopNpcTests.Run`.
Play-Mode-Pruefung ohne `-quit`: `DriftSkate.EditorTools.BusStopNpcTests.PlayRun`.
Ergebnisse und Bilder liegen unter `Logs/bus_stop`. Vor Play-Mode-Tests den
Spielstand sichern und danach zurueckspielen.
