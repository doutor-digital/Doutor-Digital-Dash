namespace LeadAnalytics.Api.Models;

/// <summary>
/// Cache do criativo de um anúncio da Meta: nome e miniatura.
///
/// O rastreio guarda no lead só o id do anúncio. Id não diz nada para quem olha o
/// dashboard — quem lembra da peça lembra da imagem. Buscar na Graph API a cada
/// carregamento seria uma chamada externa dentro do caminho do dashboard, então o
/// resultado mora aqui e é renovado por idade.
///
/// A URL da miniatura é assinada e expira; por isso <see cref="FetchedAt"/> existe e
/// o registro é revisitado depois de alguns dias, mesmo quando a busca deu certo.
/// <see cref="NotFound"/> marca o id que a Meta não reconhece (anúncio apagado, ou um
/// id que nem é anúncio) para não insistir a cada carregamento.
/// </summary>
public class AdCreative
{
    public int Id { get; set; }

    /// <summary>Id do objeto na Meta (anúncio ou publicação).</summary>
    public string AdId { get; set; } = null!;

    /// <summary>Nome do anúncio na Meta, quando existe.</summary>
    public string? Name { get; set; }

    /// <summary>URL da miniatura (CDN da Meta) — expira, por isso é revalidada.</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Link para a peça (publicação/permalink), quando a Meta devolve.</summary>
    public string? PermalinkUrl { get; set; }

    /// <summary>Id não reconhecido pela Meta — para de tentar até a próxima revalidação.</summary>
    public bool NotFound { get; set; }

    /// <summary>Quando o dado foi buscado — base da revalidação.</summary>
    public DateTime FetchedAt { get; set; }
}
