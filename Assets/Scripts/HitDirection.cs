// Which quarter a hit came from, relative to the victim's own forward.
// Front/Back/Left/Right are the one-hot observation order (#43), so changing
// the declaration order invalidates trained models. None is the "no hit
// remembered, or shooter unknown" case and is emitted as all-zeros.
public enum HitDirection
{
    None,
    Front,
    Back,
    Left,
    Right,
}
