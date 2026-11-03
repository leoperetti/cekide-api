namespace CekideApi.Domain;

public enum PaymentStatus
{
    Paid,
    Voided
}

public enum PaymentKind
{
    Monthly,
    Pass
}

public enum PaymentAction
{
    Created,
    Modified,
    Voided
}
