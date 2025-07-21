using Crawler.Api.Core.Interfaces;
using Crawler.Application.Interfaces;
using Crawler.Worker.Models;
using Cronos;

namespace Crawler.Worker;

public class InstagramWorker : BackgroundService
{
    private readonly ILogger<InstagramWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    public InstagramWorker(ILogger<InstagramWorker> logger, IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory; // Usamos a factory para criar um escopo para cada execu��o
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cronExpression = _configuration["InstagramJob:CronExpression"];
        if (string.IsNullOrEmpty(cronExpression) || !_configuration.GetValue<bool>("InstagramJob:IsEnabled"))
        {
            _logger.LogWarning("Worker do Instagram est� desabilitado.");
            return;
        }

        var cron = CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var utcNow = DateTime.UtcNow;
            var nextUtc = cron.GetNextOccurrence(utcNow);

            if (!nextUtc.HasValue) return;

            var delay = nextUtc.Value - utcNow;
            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Pr�xima execu��o do InstagramWorker em: {delay}", delay);
                await Task.Delay(delay, stoppingToken);
            }

            _logger.LogInformation("Iniciando ciclo do InstagramWorker: {time}", DateTimeOffset.Now);

            using (var scope = _scopeFactory.CreateScope())
            {
                var crawlerService = scope.ServiceProvider.GetRequiredService<ICrawlerService>();
                // Pegamos nosso servi�o de aplica��o que cont�m a l�gica de neg�cio
                var appService = scope.ServiceProvider.GetRequiredService<ICrawlingApplicationService>();

                var targets = _configuration.GetSection("InstagramJob:Targets").Get<List<InstagramTarget>>();
                if (targets is null || !targets.Any())
                {
                    _logger.LogWarning("Nenhum alvo configurado para o InstagramJob.");
                    continue;
                }

                foreach (var target in targets)
                {
                    try
                    {
                        _logger.LogInformation("Buscando dados para o alvo: {Username}", target.Username);
                        // 1. Buscamos o JSON bruto
                        string resultJson = await crawlerService.CrawlAndGetResultAsync(target.Username, target.MaxItems);

                        // 2. Delegamos a l�gica de processar e salvar para o servi�o de aplica��o
                        await appService.ProcessAndSaveCrawlResultAsync(resultJson);

                        _logger.LogInformation("Processamento do alvo {Username} conclu�do.", target.Username);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Falha ao processar o alvo {Username}", target.Username);
                    }
                }
            }
        }
    }
}