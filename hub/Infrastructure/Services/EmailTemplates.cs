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

namespace PlaywrightHub.Infrastructure.Services;

/// <summary>
///     HTML and plain text email templates
/// </summary>
public static class EmailTemplates
{
    public static string GetPasswordResetHtml(string userName, string resetUrl, string token)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Reset Your Password</title>
    <style>
        body {{
            margin: 0;
            padding: 0;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background-color: #f5f5f5;
            line-height: 1.6;
        }}
        .email-container {{
            max-width: 600px;
            margin: 40px auto;
            background: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            padding: 40px 30px;
            text-align: center;
            color: #ffffff;
        }}
        .header h1 {{
            margin: 0;
            font-size: 28px;
            font-weight: 700;
        }}
        .header p {{
            margin: 8px 0 0;
            font-size: 14px;
            opacity: 0.9;
        }}
        .content {{
            padding: 40px 30px;
        }}
        .greeting {{
            font-size: 18px;
            font-weight: 600;
            color: #1a202c;
            margin-bottom: 20px;
        }}
        .message {{
            font-size: 15px;
            color: #4a5568;
            margin-bottom: 30px;
        }}
        .cta-container {{
            text-align: center;
            margin: 30px 0;
        }}
        .cta-button {{
            display: inline-block;
            padding: 16px 32px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #ffffff !important;
            text-decoration: none;
            border-radius: 8px;
            font-size: 16px;
            font-weight: 600;
            box-shadow: 0 4px 12px rgba(102, 126, 234, 0.3);
            transition: transform 0.2s ease;
        }}
        .cta-button:hover {{
            transform: translateY(-2px);
            box-shadow: 0 6px 16px rgba(102, 126, 234, 0.4);
        }}
        .info-box {{
            background: #edf2f7;
            border-left: 4px solid #667eea;
            padding: 16px 20px;
            margin: 20px 0;
            border-radius: 4px;
        }}
        .info-box p {{
            margin: 0;
            font-size: 14px;
            color: #2d3748;
        }}
        .info-box strong {{
            color: #1a202c;
        }}
        .security-notice {{
            background: #fff5f5;
            border-left: 4px solid #f56565;
            padding: 16px 20px;
            margin: 20px 0;
            border-radius: 4px;
            font-size: 13px;
            color: #742a2a;
        }}
        .footer {{
            background: #f7fafc;
            padding: 30px;
            text-align: center;
            border-top: 1px solid #e2e8f0;
        }}
        .footer p {{
            margin: 0 0 10px;
            font-size: 13px;
            color: #718096;
        }}
        .footer a {{
            color: #667eea;
            text-decoration: none;
        }}
        .footer a:hover {{
            text-decoration: underline;
        }}
        .logo {{
            width: 48px;
            height: 48px;
            margin: 0 auto 15px;
        }}
        @media only screen and (max-width: 600px) {{
            .email-container {{
                margin: 20px;
                border-radius: 8px;
            }}
            .header {{
                padding: 30px 20px;
            }}
            .content {{
                padding: 30px 20px;
            }}
            .header h1 {{
                font-size: 24px;
            }}
            .cta-button {{
                padding: 14px 28px;
                font-size: 15px;
            }}
        }}
    </style>
</head>
<body>
    <div class=""email-container"">
        <div class=""header"">
            <h1>🔒 Password Reset</h1>
            <p>Agenix Playwright Service</p>
        </div>
        <div class=""content"">
            <div class=""greeting"">Hi {userName},</div>
            <div class=""message"">
                <p>We received a request to reset your password for your Agenix Playwright Service account.</p>
                <p>Click the button below to create a new password:</p>
            </div>
            <div class=""cta-container"">
                <a href=""{resetUrl}"" class=""cta-button"">Reset Your Password</a>
            </div>
            <div class=""info-box"">
                <p><strong>This link will expire in 1 hour.</strong></p>
                <p>For security reasons, password reset links are valid for a limited time only.</p>
            </div>
            <div class=""security-notice"">
                <strong>Didn't request this?</strong><br>
                If you didn't ask to reset your password, you can safely ignore this email. Your password will remain unchanged.
            </div>
        </div>
        <div class=""footer"">
            <p><strong>Agenix Playwright Service</strong></p>
            <p>Test the Pieces. Verify the Whole.</p>
            <p style=""margin-top: 15px; font-size: 12px;"">
                This email was sent automatically. Please do not reply.
            </p>
        </div>
    </div>
</body>
</html>";
    }

    public static string GetPasswordResetText(string userName, string resetUrl)
    {
        return $@"
Password Reset - Agenix Playwright Service

Hi {userName},

We received a request to reset your password for your Agenix Playwright Serivice account.

To reset your password, please visit the following link:
{resetUrl}

This link will expire in 1 hour for security reasons.

If you didn't request this password reset, you can safely ignore this email. Your password will remain unchanged.

---
Agenix Playwright Service
Test the Pieces. Verify the Whole.

This email was sent automatically. Please do not reply.
";
    }
}
