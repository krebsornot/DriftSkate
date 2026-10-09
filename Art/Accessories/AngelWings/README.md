# Engelsfluegel / Angel Wings

Eigenstaendiges Accessoire im DriftSkate-Toon-Stil. Unity-Prefab:
Assets/_Game/Accessories/AngelWings/AngelWings.prefab

Das Prefab in eine Szene ziehen und Play starten: WingFlap spielt automatisch in einer Schleife.
In Blender Art/Accessories/AngelWings/AngelWings.blend oeffnen, das Rig auswaehlen und
Pose Mode nutzen. Die Timeline enthaelt einen Fluegelschlag (Frames 1 bis 49, 30 fps).

21 Knochen: WingRoot sowie je Seite Shoulder, Elbow, Tip und sieben Feather-Knochen.
Alle 4896 Vertices sind gewichtet. 9520 Dreiecke, ein Skinned Mesh, drei Toon-Materialien.
Die einzelnen Federn sind starr gewichtet, damit sie beim Schlagen ihre Form behalten.
Die Gelenke werden von ueberlappenden Deckfedern verdeckt.

Der Ursprung ist der Befestigungspunkt am oberen Ruecken. Unity: Y oben, +Z nach vorne.
Als Kind an einen Brust-/Rueckenknochen setzen und lokal am Ruecken ausrichten;
Groesse und Abstand an die jeweilige Figur anpassen. Das Accessoire ist noch nicht
an den Spieler montiert. Die eigene Generic-Animation bleibt am Fluegel-Prefab.

Blender-Quelle und FBX sind beide enthalten. Zum Neuaufbau:
Tools/Blender/build_angel_wings.py mit Blender im Hintergrund ausfuehren,
danach Unity-Menue DriftSkate > Angel Wings > Prefab erstellen.
Der Generator ueberschreibt seine eigenen Ausgaben; manuell bearbeitete Varianten vorher separat speichern.
