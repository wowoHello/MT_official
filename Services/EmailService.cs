using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace MT.Services
{
    public interface IEmailService
    {
        Task SendResetPWEmailAsync(string? toEmail, string subject, string basehref, string resetPasswordURL);
    }

    public class EmailService : IEmailService
    {
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string? _smtpUser;
        private readonly string? _smtpPassword;

        /// <summary>
        /// 建構函式，用於初始化 SMTP 設定
        /// </summary>
        public EmailService()
        {
            _smtpServer = "smtp.gmail.com";
            _smtpPort = 587;
            _smtpUser = "pig22630182@gmail.com";
            _smtpPassword = "rtrs yxnp lycm hrxu";
        }

        private void EnsureSmtpConfigured()
        {
            if (string.IsNullOrWhiteSpace(_smtpUser) || string.IsNullOrWhiteSpace(_smtpPassword))
            {
                throw new InvalidOperationException("系統尚未完成 SMTP 設定，暫時無法寄送通知信。");
            }
        }

        /// <summary>
        /// 發送密碼重設郵件
        /// </summary>
        public async Task SendResetPWEmailAsync(string? toEmail, string subject, string basehref, string resetPasswordURL)
        {
            EnsureSmtpConfigured();
            var smtpUser = _smtpUser!;
            var smtpPassword = _smtpPassword!;

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("收件者 Email 不得為空");

            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Email 主題不得為空");

            string message = $@"<!DOCTYPE html>
            <html lang='zh-Hant'>
            <head>
            <meta charset='UTF-8'>
            <meta name='viewport' content='width=device-width,initial-scale=1'>
            <title>CWT 密碼重設通知</title>
            </head>
            <body style='margin:0;padding:0;background-color:#FBF9F6;font-family:Helvetica,Arial,sans-serif;color:#374151;'>
              <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='background-color:#FBF9F6;padding:32px 16px;'>
                <tr>
                  <td align='center'>
                    <table role='presentation' width='600' cellpadding='0' cellspacing='0' border='0' style='max-width:600px;background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(55,65,81,0.08);'>
                      <tr>
                        <td bgcolor='#6B8EAD' style='background-color:#6B8EAD;padding:44px 32px 36px;text-align:center;'>
                          <table role='presentation' cellpadding='0' cellspacing='0' border='0' align='center' style='margin:0 auto;'>
                            <tr>
                              <td bgcolor='#ffffff' style='background-color:#ffffff;border-radius:24px;padding:6px 18px;'>
                                <span style='color:#6B8EAD;font-size:13px;font-weight:700;letter-spacing:3px;font-family:Georgia,Helvetica,Arial,sans-serif;'>CWT</span>
                              </td>
                            </tr>
                          </table>
                          <h1 style='margin:20px 0 8px;color:#ffffff;font-size:24px;font-weight:700;letter-spacing:2px;'>命題工作平臺</h1>
                          <p style='margin:0;color:#D4E0EA;font-size:13px;letter-spacing:4px;'>密碼重設通知</p>
                        </td>
                      </tr>
                      <tr>
                        <td bgcolor='#8EAB94' style='background-color:#8EAB94;height:4px;line-height:4px;font-size:0;'>&nbsp;</td>
                      </tr>
                      <tr>
                        <td style='padding:36px 32px 8px;'>
                          <p style='margin:0 0 16px;font-size:15px;line-height:1.7;'>親愛的老師，您好：</p>
                          <p style='margin:0 0 28px;font-size:15px;line-height:1.7;'>您正在進行 CWT 命題工作平臺的密碼重設，請點選以下按鈕設定您的新密碼。</p>
                          <table role='presentation' cellpadding='0' cellspacing='0' border='0' align='center' width='100%'>
                            <tr>
                              <td align='center' style='padding:0 0 32px;'>
                                <!--[if mso]>
                                <v:roundrect xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:w=""urn:schemas-microsoft-com:office:word"" href=""{resetPasswordURL}"" style=""height:52px;v-text-anchor:middle;width:240px;"" arcsize=""15%"" stroke=""f"" fillcolor=""#6B8EAD"">
                                  <w:anchorlock/>
                                  <center style=""color:#ffffff;font-family:sans-serif;font-size:16px;font-weight:bold;letter-spacing:3px;"">重設密碼</center>
                                </v:roundrect>
                                <![endif]-->
                                <!--[if !mso]><!-->
                                <table role='presentation' cellpadding='0' cellspacing='0' border='0'>
                                  <tr>
                                    <td bgcolor='#6B8EAD' style='background-color:#6B8EAD;border-radius:8px;padding:16px 56px;text-align:center;'>
                                      <a href='{resetPasswordURL}' target='_blank' style='color:#ffffff;text-decoration:none;font-size:16px;font-weight:700;letter-spacing:3px;font-family:Helvetica,Arial,sans-serif;'>重設密碼</a>
                                    </td>
                                  </tr>
                                </table>
                                <!--<![endif]-->
                              </td>
                            </tr>
                          </table>
                          <p style='margin:0 0 8px;font-size:13px;color:#6b7280;'>若按鈕無法點擊，請複製下列網址至瀏覽器開啟：</p>
                          <div style='padding:12px 14px;background-color:#F5F3EE;border-left:3px solid #8EAB94;border-radius:4px;margin-bottom:24px;'>
                            <a href='{resetPasswordURL}' target='_blank' style='font-family:Consolas,Monaco,monospace;font-size:12px;color:#6B8EAD;word-break:break-all;text-decoration:none;line-height:1.5;'>{resetPasswordURL}</a>
                          </div>
                          <div style='padding:14px 16px;background-color:#FBF1EC;border-radius:8px;margin-bottom:24px;'>
                            <p style='margin:0;font-size:13px;line-height:1.6;color:#9C5238;'>
                              <strong>連結有效時間：10 分鐘</strong><br>
                              逾時請至登入頁重新申請密碼重設。
                            </p>
                          </div>
                          <p style='margin:0 0 16px;font-size:13px;line-height:1.7;color:#6b7280;'>
                            若您並未發起此次密碼重設，請忽略本信件，您的帳號仍然安全。
                          </p>
                          <p style='margin:0 0 24px;font-size:13px;line-height:1.7;color:#6b7280;'>
                            如有任何問題，歡迎來信客服信箱：<a href='mailto:{smtpUser}' style='color:#6B8EAD;text-decoration:none;'>{smtpUser}</a>
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style='background-color:#F5F3EE;padding:20px 32px;text-align:center;border-top:1px solid #e5e7eb;'>
                          <p style='margin:0 0 8px;'>
                            <a href='{basehref}' target='_blank' style='color:#6B8EAD;font-size:13px;font-weight:600;text-decoration:none;'>前往 CWT 命題工作平臺</a>
                          </p>
                          <p style='margin:0;font-size:12px;color:#9ca3af;line-height:1.6;'>
                            本信件由系統自動寄出，請勿直接回覆<br>
                            &copy; {DateTime.Now.Year} 全民中文檢定 CWT. All rights reserved.
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>";

            var smtpClient = new SmtpClient(_smtpServer)
            {
                Port = _smtpPort,
                Credentials = new NetworkCredential(smtpUser, smtpPassword),
                EnableSsl = true,
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(smtpUser, "CWT 命題工作平臺"),
                Subject = subject,
                Body = message,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(toEmail);

            try
            {
                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"發送 Email 失敗：{ex.Message}", ex);
            }
        }
    }
}
