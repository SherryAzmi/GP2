-- No names, phones or free-text notes are stored online: patients are a random id from the clinic PC.
CREATE TABLE IF NOT EXISTS patients (
  id TEXT PRIMARY KEY,
  operated_leg TEXT, age INTEGER, height_cm REAL, weight_kg REAL,
  surgery_type TEXT, surgery_date TEXT,
  safety_limit REAL, comfortable_max REAL, extension_goal REAL,
  updated TEXT
);

CREATE TABLE IF NOT EXISTS sessions (
  patient_id TEXT NOT NULL,
  date TEXT NOT NULL,
  game TEXT, stage INTEGER, leg TEXT, duration_s REAL,
  done INTEGER, planned INTEGER, stopped_early INTEGER, eased INTEGER,
  success_percent INTEGER, score INTEGER,
  best_bend REAL, avg_bend REAL, straightest REAL,
  safety_alerts INTEGER, balance_pauses INTEGER, pain INTEGER, settings TEXT,
  PRIMARY KEY (patient_id, date)
);
