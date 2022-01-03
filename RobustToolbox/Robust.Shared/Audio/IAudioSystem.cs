using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Robust.Shared.Audio
{
    /// <summary>
    /// Common interface for the Audio System, which is used to play sounds on clients.
    /// </summary>
    public interface IAudioSystem
    {
        /// <summary>
        /// Used in the PAS to designate the physics collision mask of occluders.
        /// </summary>
        int OcclusionCollisionMask { get; set; }

        /// <summary>
        /// Play an audio file globally, without position.
        /// </summary>
        /// <param name="playerFilter">The set of players that will hear the sound.</param>
        /// <param name="filename">The resource path to the OGG Vorbis file to play.</param>
        /// <param name="audioParams">Audio parameters to apply when playing the sound.</param>
        IPlayingAudioStream? Play(Filter playerFilter, string filename, AudioParams? audioParams = null);

        /// <summary>
        /// Play an audio file following an entity.
        /// </summary>
        /// <param name="playerFilter">The set of players that will hear the sound.</param>
        /// <param name="filename">The resource path to the OGG Vorbis file to play.</param>
        /// <param name="uid">The UID of the entity "emitting" the audio.</param>
        /// <param name="audioParams">Audio parameters to apply when playing the sound.</param>
        IPlayingAudioStream? Play(Filter playerFilter, string filename, EntityUid uid, AudioParams? audioParams = null);

        /// <summary>
        /// Play an audio file at a static position.
        /// </summary>
        /// <param name="playerFilter">The set of players that will hear the sound.</param>
        /// <param name="filename">The resource path to the OGG Vorbis file to play.</param>
        /// <param name="coordinates">The coordinates at which to play the audio.</param>
        /// <param name="audioParams">Audio parameters to apply when playing the sound.</param>
        IPlayingAudioStream? Play(Filter playerFilter, string filename, EntityCoordinates coordinates, AudioParams? audioParams = null);
    }
}
