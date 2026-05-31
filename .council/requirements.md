# Council Requirements

> Task: Speargoblin — neue Kampfeinheit (Nahkämpfer mit Reichweite) in Goblino
> Created by council-questions. Verified by council-review.

## Kernentscheidungen (vom Nutzer bestätigt)

- **Mechanik:** Nahkämpfer mit **erhöhter Reichweite** (Reach, `range` 2). **Kein Wurf**, kein `ProjectileSprite`, kein fliegender Speer.
- **Rolle/Konter:** stark gegen **Club** (Nahkampf-Goblins) und **neutrale Monster** (Bonusschaden); **schwach gegen Archer** (wird gekitet — langsam + keine Distanzwaffe).
- **Trainiert in:** Barracks (3. Trainingskarte neben Club + Archer), kein Tech-Prereq.
- **Sprite:** bereits im MiniWorldSprites-Frameset vorhanden (Speargoblin existiert) — wiederverwenden, kein neues Pixel-Art.
- **Bot:** baut ihn normal **random** in der Militär-Rotation; **bei Monster-Raids gezielt Speargoblins** (wegen Monster-Bonus).
- **Multiplayer:** voll eingehängt (NetworkCatalog defIndex↔def + NetId-Vorreservierung beim Training).

## Acceptance Criteria

### Logic
- [ ] Neue `GoblinUnitDefinition` (unter `Assets/Generated/Units/`) + `GoblinSpawner._kinds`-Eintrag (Name "Speargoblin" + WalkFrames + Definition).
- [ ] `range` = 2 (Reach). Club = 1, Archer ≈ 5. Nahkampf-Logik (kein Projektil), greift Einheiten **und** Gebäude an.
- [ ] HP **höher** als Club; Damage **höher** als Club; `attack-interval` **langsamer** als Club; Bewegungsgeschwindigkeit **langsamer** als Club.
- [ ] Bonusschaden gegen (a) neutrale Monster (`IsNeutral`) und (b) Nahkampf-Goblins (Club / Einheiten ohne `ProjectileSprite`).
- [ ] Trainierbar in Barracks; Kosten (wood/food/pop) gesetzt, im selben Korridor wie Club/Archer.
- [ ] „Fertig" = Asset + Spawner-Kind + Barracks-Karte + Bot trainiert ihn + korrekter MP-Spawn — **alle 5 Pflicht**.

### Critic (must-handle cases)
- [ ] **Reach-FSM:** range-2-Nahkampf darf NICHT in Lauf↔Stopp-Oszillation kippen; Einheit greift aus Distanz 2 an statt in die Zielzelle zu rennen. Verifiziert (kein endloses Re-Pathing).
- [ ] **NetworkCatalog:** neue Def registriert (über `MainBaseSetup.OnNewWorld`), `defIndex` deterministisch über Clients; NetId-Vorreservierung beim Training identisch (kein MP-Desync).
- [ ] **Bot kennt die Einheit:** keine tote Content / kein still ignorierter Kind-Eintrag; Bot trainiert + kommandiert ihn.
- [ ] **Balance:** Werte so, dass weder Club noch Archer obsolet werden (Spear teurer/langsamer; Archer kontert ihn; Club bleibt billige Massentruppe).
- [ ] **No friendly fire** bleibt invariant; Bonusschaden trifft nur cross-owner/neutral.
- [ ] **Reach gegen Wasser/Klippe/andere Landmasse:** kein „unangreifbares Camping" über unpassierbares Terrain hinweg, kein Hängenbleiben wenn Ziel unerreichbar.

### Feeling
- [ ] Fühlt sich als **solider Frontkämpfer** an (etwas schwerer/langsamer als Club, „hält die Linie").
- [ ] Treffer = bestehendes Hit-FX (Flash + Wobble + roter Spritz) **plus sehr leichter Knockback** am Ziel.

### Quality
- [ ] MCP-Compile sauber (0 Fehler), via Community-MCP (`mcp__UnityMCP__*`).
- [ ] Deterministische Play-Mode-Probes grün: Spawn, Owner, NetId-Reservierung, Reichweiten-/Bonus-Berechnung, Catalog-Registrierung.
- [ ] EditMode-Tests angepasst/ergänzt falls die neue Mechanik (Reach + Bonusschaden) testbare Pure-Logic einführt; Suite bleibt vollständig grün.
- [ ] `GoblinUnitDefinition`-Felder konsistent zu Club/Archer/Boat (kein Feld vergessen).
- [ ] CLAUDE.md aktualisiert (Einheitenliste, Bot-AI, ggf. Testzahl). Commit direkt auf `main`.

### Wildcard
- [ ] **Kein** Angriff von Bord eines Boots (normale Passagier-Regeln).
- [ ] **Kein** Bot-Monokultur-Kollaps: feste Random-Quote normal, Spear nur gezielt für Monster-Raids.
- [ ] **Kein** Formationsbonus (kollidiert mit Anti-Stacking — bewusst raus).
- [ ] Monster-Bonus kippt die Monster-Raid-Balance nicht ins Triviale (moderater Multiplikator).

### Optics
- [ ] Walk-Sprite aus vorhandenem MiniWorldSprites-Speargoblin-Frameset; gleiche Pivot/Padding-Konvention → korrekte `SelectionBounds`.
- [ ] Kein Projektil-Sprite (Nahkampf).
- [ ] Train-/Inspector-Icon = zugeschnittener Frame aus dem Walk-Sheet (wie Club/Archer).
- [ ] Faction-Tint: kein Sonderfall, gleiche Behandlung wie andere Einheiten.

## Known accepted edges
- **cd==1 Diagonal-über-Eckwasser:** Der Anti-Camping-Guard greift nur bei `cd > 1`. Bei Chebyshev-Distanz 1 (direkt/diagonal benachbart) schlägt der Speer zu, auch wenn die diagonale Zwischenzelle Wasser ist. Das ist **identisch zum bestehenden Club-Verhalten** (range 1) und damit kein Speer-Regress — bewusst akzeptiert.

## Out of Scope
- Wurfspeer/Projektil-Variante.
- Bonusschaden gegen Boote/Gebäude (nur Monster + Nahkampf-Goblins).
- Brace-/Charge-/Anti-Ansturm-Mechanik.
- Formationsbonus.
- Angriff von Bord eines Boots.
- Neue Pixel-Art / neues Trainingsgebäude / Tech-Tree-Gate.
