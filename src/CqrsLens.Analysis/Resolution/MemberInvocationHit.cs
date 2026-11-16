namespace CqrsLens;

internal readonly record struct MemberInvocationHit<TTarget>(
    int Index,
    TTarget Target,
    string Method,
    string? TypeArgs,
    int OpenParenIndex);
