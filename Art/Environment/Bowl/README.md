# NeonPool Bowl v2

Neu modellierter eingelassener Skatepool: asymmetrische Kontur, breiter Fahrboden, sanfte Rundungen, dunkles Coping, weiss/blaue Fliesen und pinke Fahrmarkierungen.

- Prefab: Assets/_Game/SkateparkTest/Prefabs/NeonPool_Bowl.prefab
- Testszene: Assets/_Game/SkateparkTest/Scenes/BowlTest.unity
- Austauschmodell: NeonPool_Bowl.obj und NeonPool_Bowl.mtl zusammen importieren.
- Ursprung auf Deckhoehe; Boden Y=-2,6 m; 1 Einheit = 1 Meter.
- Fahrboden circa 12 x 7 m; Oeffnung circa 21 x 16 m; Deck 2,2 m breit.
- Uebergangsradius 5,2 m, horizontaler Lauf 4,5 m, maximal 60 Grad Steigung.
- 160 Umfangssegmente, 48 Hoehenabschnitte, MeshCollider fuer Becken und Deck.
- Geschlossener GrindRail-Pfad. Fliesen und Markierungen ohne Kollisionsstufen.

Mit Skalierung 1 auf Gelaendehoehe platzieren. Darunter darf KEIN durchgehender Boden-Collider liegen: Terrain/Plattform brauchen eine Aussparung. Die Testszene enthaelt umlaufenden Boden mit passender Oeffnung. City.unity wurde nicht veraendert.

Der SkaterController erkennt nur Normalen-Y > 0,45 als fahrbar. Die neue Bowl bleibt innerhalb dieses Limits. Vier Physiktests mit dem echten Controller starten mit 6 m/s am Boden und fahren 7 Sekunden ohne Push. Ergebnisse stehen in skating-test.txt; die Geometriepruefung in validation.txt. Das prueft Uebergaenge und Bodenkontakt, ersetzt aber keinen manuellen Spieltest aller Tricks und Grinds.

DriftSkate > Skatepark-Testset > Bowl erstellen aktualisiert Bowl, Testszene und Vorschauen. OBJ enthaelt Geometrie und Farben; Collider und GrindRail sind im Unity-Prefab.
