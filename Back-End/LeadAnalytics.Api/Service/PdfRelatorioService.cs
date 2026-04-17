using LeadAnalytics.Api.DTOs.Response;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Gera o PDF do relatório mensal usando QuestPDF.
/// Stateless — todos os métodos privados são static para evitar alocações desnecessárias.
/// </summary>
public sealed class PdfRelatorioService : IPdfRelatorioService
{
    // ── Paleta ────────────────────────────────────────────────────────────────
    private static readonly Color CorPrimaria       = Color.FromHex("#1E3A5F");
    private static readonly Color CorSecundaria     = Color.FromHex("#2E86AB");
    private static readonly Color CorFundoAlternado = Color.FromHex("#F3F7FB");
    private static readonly Color CorBorda          = Color.FromHex("#D1D5DB");
    private static readonly Color CorTextoCinza     = Color.FromHex("#6B7280");

    public byte[] Gerar(RelatorioMensalDadosDto dados)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9));

                page.Header().Element(h => Cabecalho(h, dados));
                page.Content().PaddingTop(12).Element(c => Conteudo(c, dados));
                page.Footer().Element(Rodape);
            });
        })
        .GeneratePdf();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Seções principais
    // ═════════════════════════════════════════════════════════════════════════

    private static void Cabecalho(IContainer container, RelatorioMensalDadosDto dados)
    {
        container
            .BorderBottom(2).BorderColor(CorSecundaria)
            .PaddingBottom(10)
            .Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item()
                        .DefaultTextStyle(x => x.FontSize(18).Bold().FontColor(CorPrimaria))
                        .Text("Relatório Mensal de Leads");

                    col.Item().PaddingTop(3)
                        .DefaultTextStyle(x => x.FontSize(11).FontColor(CorSecundaria))
                        .Text(dados.NomeClinica);
                });

                row.ConstantItem(175).Column(col =>
                {
                    col.Item().AlignRight()
                        .DefaultTextStyle(x => x.FontSize(10).Bold().FontColor(CorPrimaria))
                        .Text($"{NomeMes(dados.Mes)} / {dados.Ano}");

                    col.Item().AlignRight().PaddingTop(3)
                        .DefaultTextStyle(x => x.FontSize(8).FontColor(CorTextoCinza))
                        .Text($"Gerado em: {dados.GeradoEm:dd/MM/yyyy HH:mm}");
                });
            });
    }

    private static void Conteudo(IContainer container, RelatorioMensalDadosDto dados)
    {
        container.Column(col =>
        {
            col.Spacing(14);

            col.Item().Element(c => CartaoesKpi(c, dados));
            col.Item().Element(c => TabelaLeadsPorOrigem(c, dados));

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => TabelaLeadsPorEtapa(c, dados));
                row.ConstantItem(10);
                row.RelativeItem().Element(c => TabelaLeadsPorUnidade(c, dados));
            });

            col.Item().Element(c => TabelaLeadsPorDia(c, dados));
            col.Item().Element(c => TabelaListagemDetalhada(c, dados));
        });
    }

    private static void Rodape(IContainer container)
    {
        container
            .BorderTop(1).BorderColor(CorBorda)
            .PaddingTop(5)
            .Row(row =>
            {
                row.RelativeItem()
                    .DefaultTextStyle(x => x.FontSize(8).FontColor(CorTextoCinza))
                    .Text("LeadAnalytics — Relatório gerado automaticamente");

                row.ConstantItem(90).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(CorTextoCinza));
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // KPI Cards
    // ═════════════════════════════════════════════════════════════════════════

    private static void CartaoesKpi(IContainer container, RelatorioMensalDadosDto dados)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c =>
                CardKpi(c, "Total de Leads", dados.TotalLeads.ToString("N0")));

            row.ConstantItem(8);

            row.RelativeItem().Element(c =>
                CardKpi(c, "Taxa de Conversão", $"{dados.TaxaConversaoPercent:F1}%"));

            row.ConstantItem(8);

            row.RelativeItem().Element(c =>
                CardKpi(c, "Ticket Médio", $"R$ {dados.TicketMedio:N2}"));
        });
    }

    private static void CardKpi(IContainer container, string titulo, string valor)
    {
        container
            .Background(CorPrimaria)
            .Padding(12)
            .Column(col =>
            {
                col.Item()
                    .DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.White))
                    .Text(titulo);

                col.Item().PaddingTop(6)
                    .DefaultTextStyle(x => x.FontSize(22).Bold().FontColor(Colors.White))
                    .Text(valor);
            });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Tabelas
    // ═════════════════════════════════════════════════════════════════════════

    private static void TabelaLeadsPorOrigem(IContainer container, RelatorioMensalDadosDto dados)
    {
        var totalGeral = dados.TotalLeads > 0 ? dados.TotalLeads : 1;

        container.Column(col =>
        {
            col.Item().Element(c => TituloSecao(c, "Leads por Origem"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                table.Header(h =>
                {
                    CabecalhoCelula(h.Cell(), "Origem");
                    CabecalhoCelula(h.Cell(), "Quantidade", centralizar: true);
                    CabecalhoCelula(h.Cell(), "% Total", centralizar: true);
                });

                var par = false;
                foreach (var item in dados.LeadsPorOrigem)
                {
                    var pct = (decimal)item.Quantidade / totalGeral * 100;
                    var bg = par ? CorFundoAlternado : Colors.White;

                    CelulaDados(table.Cell(), item.Origem, bg);
                    CelulaDados(table.Cell(), item.Quantidade.ToString("N0"), bg, centralizar: true);
                    CelulaDados(table.Cell(), $"{pct:F1}%", bg, centralizar: true);
                    par = !par;
                }
            });
        });
    }

    private static void TabelaLeadsPorEtapa(IContainer container, RelatorioMensalDadosDto dados)
    {
        container.Column(col =>
        {
            col.Item().Element(c => TituloSecao(c, "Leads por Etapa"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });

                table.Header(h =>
                {
                    CabecalhoCelula(h.Cell(), "Etapa");
                    CabecalhoCelula(h.Cell(), "Quantidade", centralizar: true);
                });

                var par = false;
                foreach (var item in dados.LeadsPorEtapa)
                {
                    var bg = par ? CorFundoAlternado : Colors.White;
                    CelulaDados(table.Cell(), item.Etapa, bg);
                    CelulaDados(table.Cell(), item.Quantidade.ToString("N0"), bg, centralizar: true);
                    par = !par;
                }
            });
        });
    }

    private static void TabelaLeadsPorUnidade(IContainer container, RelatorioMensalDadosDto dados)
    {
        container.Column(col =>
        {
            col.Item().Element(c => TituloSecao(c, "Leads por Unidade"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });

                table.Header(h =>
                {
                    CabecalhoCelula(h.Cell(), "Unidade");
                    CabecalhoCelula(h.Cell(), "Quantidade", centralizar: true);
                });

                var par = false;
                foreach (var item in dados.LeadsPorUnidade)
                {
                    var bg = par ? CorFundoAlternado : Colors.White;
                    CelulaDados(table.Cell(), item.NomeUnidade, bg);
                    CelulaDados(table.Cell(), item.QuantidadeLeads.ToString("N0"), bg, centralizar: true);
                    par = !par;
                }
            });
        });
    }

    private static void TabelaLeadsPorDia(IContainer container, RelatorioMensalDadosDto dados)
    {
        if (dados.LeadsPorDia.Count == 0) return;

        var max = Math.Max(dados.LeadsPorDia.Max(d => d.Quantidade), 1);

        container.Column(col =>
        {
            col.Item().Element(c => TituloSecao(c, "Distribuição Diária"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(48);  // Dia
                    c.RelativeColumn();    // Barra visual
                    c.ConstantColumn(52);  // Qtd
                });

                table.Header(h =>
                {
                    CabecalhoCelula(h.Cell(), "Dia", centralizar: true);
                    CabecalhoCelula(h.Cell(), "Volume");
                    CabecalhoCelula(h.Cell(), "Qtd.", centralizar: true);
                });

                var par = false;
                foreach (var item in dados.LeadsPorDia)
                {
                    // Barra proporcional: entre 2% e 100% para evitar item com peso 0
                    var pct = Math.Clamp((float)item.Quantidade / max, 0.02f, 1f);
                    var bg = par ? CorFundoAlternado : Colors.White;

                    // Coluna: Dia
                    table.Cell()
                        .Background(bg).Padding(4)
                        .AlignCenter()
                        .DefaultTextStyle(x => x.FontSize(8))
                        .Text($"Dia {item.Dia:D2}");

                    // Coluna: Barra visual
                    table.Cell().Background(bg).Padding(4).Row(barRow =>
                    {
                        barRow.RelativeItem(pct).Background(CorSecundaria).Height(10);
                        // Espaço restante apenas quando a barra não ocupa tudo
                        if (pct < 1f)
                            barRow.RelativeItem(1f - pct).Height(10);
                    });

                    // Coluna: Quantidade
                    table.Cell()
                        .Background(bg).Padding(4)
                        .AlignCenter()
                        .DefaultTextStyle(x => x.FontSize(8))
                        .Text(item.Quantidade.ToString("N0"));

                    par = !par;
                }
            });
        });
    }

    private static void TabelaListagemDetalhada(IContainer container, RelatorioMensalDadosDto dados)
    {
        container.Column(col =>
        {
            col.Item().Element(c =>
                TituloSecao(c, $"Listagem Detalhada  ({dados.TotalLeads:N0} leads)"));

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);  // Nome
                    c.RelativeColumn(2);  // Telefone
                    c.RelativeColumn(2);  // Origem
                    c.RelativeColumn(2);  // Etapa
                    c.RelativeColumn(2);  // Data
                });

                // O header é repetido automaticamente em cada página pelo QuestPDF
                table.Header(h =>
                {
                    CabecalhoCelula(h.Cell(), "Nome");
                    CabecalhoCelula(h.Cell(), "Telefone");
                    CabecalhoCelula(h.Cell(), "Origem");
                    CabecalhoCelula(h.Cell(), "Etapa");
                    CabecalhoCelula(h.Cell(), "Data de Criação");
                });

                var par = false;
                foreach (var lead in dados.Leads)
                {
                    var bg = par ? CorFundoAlternado : Colors.White;

                    CelulaDados(table.Cell(), lead.Nome, bg);
                    CelulaDados(table.Cell(), lead.Telefone ?? "–", bg);
                    CelulaDados(table.Cell(), lead.Origem, bg);
                    CelulaDados(table.Cell(), lead.Stage, bg);
                    CelulaDados(table.Cell(), lead.CriadoEm.ToString("dd/MM/yyyy HH:mm"), bg);
                    par = !par;
                }
            });
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Componentes reutilizáveis
    // ═════════════════════════════════════════════════════════════════════════

    private static void TituloSecao(IContainer container, string titulo)
    {
        container
            .Background(CorPrimaria)
            .Padding(6)
            .DefaultTextStyle(x => x.FontSize(10).Bold().FontColor(Colors.White))
            .Text(titulo);
    }

    private static void CabecalhoCelula(IContainer container, string texto, bool centralizar = false)
    {
        var c = container
            .Background(CorSecundaria)
            .Border(1).BorderColor(CorBorda)
            .Padding(5);

        if (centralizar)
            c = c.AlignCenter();

        c.DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(Colors.White))
         .Text(texto);
    }

    private static void CelulaDados(IContainer container, string texto, Color bg, bool centralizar = false)
    {
        var c = container
            .Background(bg)
            .Border(1).BorderColor(CorBorda)
            .Padding(4);

        if (centralizar)
            c = c.AlignCenter();

        c.DefaultTextStyle(x => x.FontSize(8))
         .Text(texto);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Utilitários
    // ═════════════════════════════════════════════════════════════════════════

    private static string NomeMes(int mes) => mes switch
    {
        1  => "Janeiro",
        2  => "Fevereiro",
        3  => "Março",
        4  => "Abril",
        5  => "Maio",
        6  => "Junho",
        7  => "Julho",
        8  => "Agosto",
        9  => "Setembro",
        10 => "Outubro",
        11 => "Novembro",
        12 => "Dezembro",
        _  => $"Mês {mes}"
    };
}
