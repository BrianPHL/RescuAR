namespace RescuAR.App.Models;

public sealed record InformationSection(string Number, string Title, string Description, IReadOnlyList<string> BulletPoints)
{
    public bool HasBullets => BulletPoints.Count > 0;
}
