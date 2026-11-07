namespace CCCPRacing.UI;

// The Party Meeting's rows by identity (owner's menu restructure 2026-10-27, second hierarchy). RaceScene decides which rows exist and in what order (RaceScene.Pause.cs) and acts on a selected
// row by its id; the Hud draws the labels of those ids and knows nothing of positions. One list, three readers: the draw, the mouse hit test and the actions.

/// <summary>The main menu, top to bottom as they appear: REPLAY first when it applies, NORMS AND MEDALS, DRIVING SCHOOL, SETTINGS, then CONTINUE, RESTART RACE, EXIT RACE (KEYS lives under SETTINGS).</summary>
public enum PauseRowId { Replay, Norms, Instructions, Settings, Continue, Restart, Exit, Leaderboards }

/// <summary>SETTINGS: the two screens, RUMBLE (only where there is a pad, as before), the KEYS help and back to the main menu.</summary>
public enum SettingsRowId { Sounds, Visuals, Lab, LabTune, Rumble, Keys, Back }

/// <summary>VISUALS: map, comments, the camera (only where there is one to choose), rendering (pixel or native), edges, softening, back. Native rendering, edges and softening trigger the CAPITALIST LUXURIES ENABLED notice.</summary>
public enum VisualRowId { Map, Comments, Camera, Edges, Softening, CarInstruments, LapInfo, NormsCard, Back, Rendering }
