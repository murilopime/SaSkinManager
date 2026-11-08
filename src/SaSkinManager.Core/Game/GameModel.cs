namespace SaSkinManager.Core.Game;

public enum ModelKind
{
    Ped,
    /// <summary>CJ: montado por peças de roupa no player.img.</summary>
    Player,
    /// <summary>Reservado para a v3 (carros, barcos, aviões…).</summary>
    Vehicle,
}

/// <summary>Um modelo do jogo que pode ser trocado (personagem hoje, veículo no futuro).</summary>
public sealed class GameModel
{
    public int Id { get; init; }
    public string ModelName { get; init; } = "";
    public string TxdName { get; init; } = "";
    public ModelKind Kind { get; init; }
    public string Category { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string PedType { get; init; } = "";

    /// <summary>IMG onde ficam os arquivos originais.</summary>
    public string ImgName => Kind == ModelKind.Player ? "player.img" : "gta3.img";

    public string Key => $"{Kind}:{ModelName}".ToLowerInvariant();

    public override string ToString() => $"{DisplayName} ({ModelName})";
}
