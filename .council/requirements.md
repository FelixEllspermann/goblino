# Council Requirements

> Task: Armor as a per-unit stat for goblins (percentage damage reduction)
> Created by council-questions (lightweight — 2 clarifying questions). Verified by council-review.
> (Previous feature "Speargoblin" is shipped — commit 15c4d09.)

## Kernentscheidungen (vom Nutzer bestätigt)

- **Modell:** Diminishing Returns — `reduction = armor / (armor + 36)`. Asymptote, erreicht 100% nie.
- **Zielwerte:** Archer **0** (keine), Club armor **4** (≈ 10%), Speargoblin armor **15** (≈ 29% — bewusst deutlich über Club, damit der Unterschied im Kampf sichtbar ist).
- **Rundung:** `Apply` **rundet ab (floor)** zugunsten des Verteidigers — so reduziert auch kleine Rüstung jeden Ganzzahl-Treffer zuverlässig (kaufmännische Rundung ließ Club 10% gegen 5er/4er-Treffer wirkungslos). Über-Erfüllung bei kleinen Treffern ist bei Integer-Schaden inhärent + akzeptiert.
- **Untergrenze/Deckel:** harter Deckel **80%** Reduktion (nie immun), **min. 1 Schaden** pro Treffer.
- **Seite:** Rüstung wirkt auf der **Verteidiger-Seite** (im `TakeDamage`), also NACH dem Spear-Angriffsbonus.

## Acceptance Criteria

### Logic
- [ ] `GoblinUnitDefinition.Armor` (float, default 0) — Archer 0, Club 4, Spear 15.
- [ ] `ArmorMath.Reduction(armor) = armor/(armor+36)`, geclamped auf ≤ 0.8.
- [ ] `ArmorMath.Apply(dmg, armor)` = `floor(dmg * (1-reduction))`, min 1 (bei dmg>0), 0 bleibt 0.
- [ ] Club und Spear unterscheiden sich bei jedem üblichen Treffer (5/4/8 dmg) sichtbar.
- [ ] `Goblin.TakeDamage` reduziert eingehenden Schaden per Armor; `Goblin.Armor` aus der Definition gesetzt.
- [ ] Reduktion erreicht nie 100% (Formel + Deckel).

### Critic (must-handle cases)
- [ ] **MP-Lockstep:** Armor wird genau EINMAL angewandt (Verteidiger-`TakeDamage`), der Wire-Betrag ist pre-armor; lokaler + Remote-Pfad (`EvDamage`→`ApplyDamage`→`TakeDamage`) ergeben identische HP.
- [ ] Kein Double-Dip mit dem Spear-Bonus (Bonus = Angreifer-Seite, Armor = Verteidiger-Seite, getrennt).
- [ ] Min-1-Floor: ein Treffer macht immer ≥ 1 Schaden, selbst bei sehr hoher Rüstung.
- [ ] 0-Schaden bleibt 0 (kein erzwungenes 1 für Nicht-Kampf-Interaktionen).
- [ ] Bestehende Einheiten ohne Armor-Key im Asset → Default 0 (kein Verhalten geändert).

### Feeling
- [ ] Rüstung als feiner Tank-Edge spürbar: Spear hält länger, Club etwas, Archer gar nicht.
- [ ] Im Stat-Sheet sichtbar (Armor: X%), damit der Spieler den Effekt versteht.

### Quality
- [ ] MCP-Compile 0 Fehler; EditMode-Tests grün (neue ArmorMath-Tests + bestehende = 63).
- [ ] `ArmorMath` Unity-frei + deterministisch + dokumentiert; Felder konsistent.
- [ ] CLAUDE.md aktualisiert (Armor-Feld, Damage-Flow, Testzahl 63). Commit auf `main`.

### Wildcard
- [ ] Monster/Farmer/Boote bleiben armorlos (0), kein ungewollter Tank-Effekt.
- [ ] Gebäudeschaden unberührt (Buildings sind keine Goblins, eigener HP-Pfad).
- [ ] Hohe-Rüstung-Edge: kein Integer-Overflow/negativer Schaden, kein Heilen durch Rundung.

### Optics
- [ ] Stat-Sheet zeigt „Armor: X%" nur wenn Armor > 0; Layout konsistent mit HP/Damage/Range.

## Known accepted decisions
- **Spear gewinnt 1v1 klar gegen Club** (Spear macht ~12–14/Treffer an Club, Club nur ~3 an Spear). Das ist **gewollt** — die designte Rolle des Spears ist „stark gegen Club" (Bonus 1.75 + 60 HP + ~29% Rüstung), balanciert durch höhere Kosten (110 Food/3 Pop), langsame Bewegung/Trainingszeit und das Gekitet-werden durch Archer. Magnitude im Playtest gegenprüfen; bei Bedarf Spear-Armor (15) oder -Bonus (1.75) zurücknehmen.
- **Floor-Über-Erfüllung:** angezeigte %-Reduktion ist der nominale Wert; effektiv ist sie bei kleinen Ganzzahltreffern etwas höher (zugunsten des Verteidigers). Bewusst akzeptiert.

## Out of Scope
- Rüstung gegen Gebäude / für Gebäude.
- Schadenstyp-spezifische Rüstung (pierce/blunt), Rüstungsdurchdringung.
- Rüstung für Monster/Farmer/Boote.
