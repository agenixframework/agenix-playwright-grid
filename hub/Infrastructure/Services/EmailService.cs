#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     Service for sending emails using SMTP
/// </summary>
public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetToken, string resetUrl);
}

public class EmailService : IEmailService
{
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly ILogger<EmailService> _logger;
    private readonly string _smtpHost;
    private readonly string _smtpPassword;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly bool _useSsl;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _smtpHost = config["SMTP_HOST"] ?? throw new ArgumentException("SMTP_HOST is required");
        _smtpPort = int.TryParse(config["SMTP_PORT"], out var port) ? port : 587;
        _smtpUsername = config["SMTP_USERNAME"] ?? throw new ArgumentException("SMTP_USERNAME is required");
        _smtpPassword = config["SMTP_PASSWORD"] ?? throw new ArgumentException("SMTP_PASSWORD is required");
        _fromEmail = config["SMTP_FROM_EMAIL"] ?? throw new ArgumentException("SMTP_FROM_EMAIL is required");
        _fromName = config["SMTP_FROM_NAME"] ?? "Agenix Playwright Grid";
        _useSsl = bool.TryParse(config["SMTP_USE_SSL"], out var useSsl) ? useSsl : true;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetToken, string resetUrl)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _fromEmail));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = "Reset Your Password - Agenix Playwright Grid";

            var htmlBody = EmailTemplates.GetPasswordResetHtml(toName, resetUrl, resetToken);
            var textBody = EmailTemplates.GetPasswordResetText(toName, resetUrl);

            var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();

            // Connect to SMTP server
            if (_useSsl)
            {
                await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.StartTls);
            }
            else
            {
                await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.None);
            }

            // Authenticate
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);

            // Send email
            await client.SendAsync(message);

            // Disconnect
            await client.DisconnectAsync(true);

            _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            throw;
        }
    }
}
