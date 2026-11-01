-- Sumas por día; ninguna fila representa a una persona.
CREATE TABLE IF NOT EXISTS counts (
  day      TEXT    NOT NULL,          -- AAAA-MM-DD (UTC)
  version  TEXT    NOT NULL,
  edition  TEXT    NOT NULL,          -- free | pro | github
  lang     TEXT    NOT NULL,
  daily    INTEGER NOT NULL DEFAULT 0, -- primeros avisos del día  → activos ese día
  monthly  INTEGER NOT NULL DEFAULT 0, -- primeros avisos del mes  → activos ese mes
  installs INTEGER NOT NULL DEFAULT 0, -- instalaciones nuevas
  pings    INTEGER NOT NULL DEFAULT 0, -- avisos recibidos
  PRIMARY KEY (day, version, edition, lang)
);
