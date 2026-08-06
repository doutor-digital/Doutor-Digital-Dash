using System.Runtime.CompilerServices;

// As regras que decidem número (leitura de campo da Kommo, rótulo de etapa) são internas:
// não fazem parte da API pública do serviço, mas precisam de teste — foi justamente onde os
// erros de contagem apareceram.
[assembly: InternalsVisibleTo("LeadAnalytics.Tests")]
