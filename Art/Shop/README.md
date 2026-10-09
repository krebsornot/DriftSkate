# CAN CLUB – Spray-Shop

In City.unity im tuerkisfarbenen Gebaeude direkt am zentralen Platz.
Eingang: Unity-Weltposition (-16.5, 0.18, 64), zeigt Richtung -Z.
B: vom Board absteigen; die Tuer oeffnet automatisch bei Annaeherung.
An der Kasse E / Gamepad X: Ladenmenue.
Pfeile / D-Pad waehlen, Enter / Gamepad A kaufen, Esc / Gamepad B schliessen.
Alternativ die Schaltflaechen mit der Maus bedienen.

Preise: 1 Dose = 100, 6 Dosen = 500, 12 Dosen = 900 Spielgeld.
Maximal 99 Dosen, Startvorrat 12 (auch fuer alte Spielstaende).
Ein erfolgreich gespruehtes Graffiti verbraucht eine Dose. Ungueltige Spruehversuche kosten nichts.
Das vorhandene Graffiti-Design bleibt erhalten; die Farben im Regal sind Dekoration.
Geld und Dosen werden gemeinsam im Profil gespeichert, der Vorrat steht im HUD.

Die bestehende City-Szene wurde lokal umgebaut; keine volle Stadt-Neugenerierung.
Der urspruengliche Hausquader bleibt deaktiviert als SprayShop_OriginalBuilding erhalten.
Eine Sicherung der Szene vor dem Umbau liegt unter Logs/City_before_spray_shop_*.unity.
Generierte Shop-Materialien und angepasste Fassaden-Meshes: Assets/_Game/Shop.
Nach einer kompletten Stadt-Neugenerierung den Shop ueber
DriftSkate > Spray Shop > In Stadt einbauen wieder installieren.

Pruefungen: Unity-Kompilierung; freie Eingangsgasse; Kauf, Geldmangel, Kapazitaet,
JSON-Speicherung; Play-Test mit Stadtstart, durch die Tuer laufen, Kassenkauf,
Geld-/Vorratsspeicherung, Spruehen und leerem Vorrat. Tests verwenden eigene Spielstaende in Logs.
