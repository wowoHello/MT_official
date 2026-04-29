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
        public EmailService(IConfiguration configuration)
        {
            _smtpServer = configuration["Smtp:Server"] ?? "smtp.gmail.com";
            _smtpPort = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 587;
            _smtpUser = configuration["Smtp:User"];
            _smtpPassword = configuration["Smtp:Password"];
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

            string message = $@"
                    親愛的 老師 您好：<br /><br />
                    您正在進行 CWT 命題工作平臺 的密碼重設。請點選以下連結設定您的新密碼：<br /><br />
                    <strong><a href='{resetPasswordURL}'>點我重設密碼</a></strong><br /><br />
                    若上述連結無法點擊，請複製下列網址至瀏覽器開啟：<br />
                    <a href='{resetPasswordURL}'>{resetPasswordURL}</a><br /><br />
                    ※ 本連結 10 分鐘內有效，逾時請重新申請。<br /><br />
                    若您並未發起此次密碼重設，請忽略本信件，您的帳號仍然安全。<br /><br />
                    若有任何問題，歡迎來信客服信箱：{smtpUser}<br />
                    <a href='{basehref}'>CWT 命題工作平臺</a><br /><br />
                    ***本封信由系統自動寄出，請勿直接回覆!***<br />";

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
