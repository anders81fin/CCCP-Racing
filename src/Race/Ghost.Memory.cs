using CCCPRacing.Car;

namespace CCCPRacing.Race;

public partial class Ghost
{
    long _recordingDamageBytes;
    CarDamage.Geometry _recordingLastShape;
    /// <summary>Conservative managed-data estimate for an incremental recording, not a deserialized tape.
    /// Shared chips/shapes may be overcounted deliberately; this is a live-recording safety budget.</summary>
    internal long RecordingEstimatedBytes=>Count*400L+_recordingDamageBytes;
    void AccountDamage(CarDamage.State state)
    {
        _recordingDamageBytes+=128L+state.Parts.Length*96L+state.Chips.Length*112L;
        if(!ReferenceEquals(_recordingLastShape,state.Shape)) {
            _recordingDamageBytes+=64+state.Shape.Data.LongLength;_recordingLastShape=state.Shape;
        }
    }
}
