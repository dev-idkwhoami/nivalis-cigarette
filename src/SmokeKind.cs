namespace NivalisMods.Cigarette;

internal enum SmokeKind { Cigarette, Joint, Mask }

#if !CIGARETTE_API
// Internal lifecycle reasons keep the shared controller independent of the optional public contract.
internal enum SmokingEndReason
{
    Completed, UserCancelled, ApiStopped, AvailabilityChanged, GameplayUnavailable,
    SceneChanged, ControllerDisabled, Failed
}
#endif
