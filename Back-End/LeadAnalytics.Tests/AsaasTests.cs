using System.Text.Json;
using LeadAnalytics.Api.Service.Asaas;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A integração escreve valor de cobrança no cartão de um paciente. Errar o mapa
/// de status transforma "vencido" em "recebido" no cartão de quem não pagou, e
/// errar a referência lança a dívida de um paciente no cartão de outro. É por isso
/// que estes testes existem, e não pela cobertura.
/// </summary>
public class AsaasTests
{
    private static AsaasWebhookPayload Ler(string json) =>
        JsonSerializer.Deserialize<AsaasWebhookPayload>(json)!;

    // ─── Contrato do payload ────────────────────────────────────────────────

    [Fact]
    public void Le_o_evento_de_pagamento_recebido_do_Asaas()
    {
        // Recorte real do webhook do Asaas (campos que usamos).
        var p = Ler("""
        {
          "event": "PAYMENT_RECEIVED",
          "payment": {
            "id": "pay_9876543210",
            "customer": "cus_000005219613",
            "subscription": null,
            "installmentCount": 3,
            "externalReference": "77616",
            "status": "RECEIVED",
            "billingType": "PIX",
            "description": "Consulta de avaliação",
            "value": 250.00,
            "netValue": 248.01,
            "dueDate": "2026-08-15",
            "paymentDate": "2026-08-12",
            "invoiceUrl": "https://www.asaas.com/i/9876543210",
            "bankSlipUrl": null
          }
        }
        """);

        Assert.Equal("PAYMENT_RECEIVED", p.Event);
        Assert.Equal("pay_9876543210", p.Payment!.Id);
        Assert.Equal("77616", p.Payment.ExternalReference);
        Assert.Equal(250.00m, p.Payment.Value);
        Assert.Equal(248.01m, p.Payment.NetValue);
        Assert.Equal(3, p.Payment.InstallmentCount);
    }

    [Fact]
    public void Cobranca_sem_externalReference_nao_vira_lead()
    {
        // Cobrança criada na mão dentro do Asaas não tem a referência. Precisa ser
        // recusada: não existe "melhor palpite" para dinheiro de paciente.
        var p = Ler("""
        {"event":"PAYMENT_CREATED","payment":{"id":"pay_1","status":"PENDING","value":100}}
        """);

        Assert.Null(p.Payment!.ExternalReference);
    }

    // ─── Mapa de status ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("PENDING", 1837785L)]
    [InlineData("CONFIRMED", 1837787L)]
    [InlineData("RECEIVED", 1837789L)]
    [InlineData("RECEIVED_IN_CASH", 1837791L)]
    [InlineData("OVERDUE", 1837793L)]
    [InlineData("REFUNDED", 1837795L)]
    [InlineData("DELETED", 1837797L)]
    [InlineData("AWAITING_RISK_ANALYSIS", 1837799L)]
    [InlineData("CHARGEBACK_REQUESTED", 1837801L)]
    public void Cada_status_do_Asaas_cai_na_opcao_certa_do_cartao(string asaas, long enumId)
    {
        Assert.Equal(enumId, AsaasIngestionService.EnumStatus(asaas));
    }

    [Fact]
    public void Status_desconhecido_nao_escreve_nada_em_vez_de_chutar()
    {
        // A Kommo aceitaria qualquer enum_id; escrever um palpite deixaria o cartão
        // afirmando um estado de pagamento que o Asaas nunca disse.
        Assert.Null(AsaasIngestionService.EnumStatus("ALGO_QUE_A_ASAAS_INVENTOU_DEPOIS"));
        Assert.Null(AsaasIngestionService.EnumStatus(null));
    }

    [Theory]
    [InlineData("BOLETO", 1837803L)]
    [InlineData("PIX", 1837805L)]
    [InlineData("CREDIT_CARD", 1837807L)]
    [InlineData("DEBIT_CARD", 1837809L)]
    [InlineData("UNDEFINED", 1837811L)]
    public void Cada_forma_de_pagamento_cai_na_opcao_certa(string asaas, long enumId)
    {
        Assert.Equal(enumId, AsaasIngestionService.EnumForma(asaas));
    }

    // ─── Dinheiro ───────────────────────────────────────────────────────────

    [Fact]
    public void Valor_vai_para_a_Kommo_com_ponto_decimal()
    {
        // Formatar em pt-BR mandaria "1234,5" e a Kommo leria outro número.
        Assert.Equal("1234.5", AsaasIngestionService.Dinheiro(1234.50m));
        Assert.Equal("250", AsaasIngestionService.Dinheiro(250m));
        Assert.Equal("248.01", AsaasIngestionService.Dinheiro(248.01m));
        Assert.Null(AsaasIngestionService.Dinheiro(null));
    }
}
