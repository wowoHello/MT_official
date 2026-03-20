using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace MT.Services
{
    public interface IEmailService
    {
        Task SendVerifyEmailAsync(string? toEmail, string subject, string basehref, string verifyCode);
        Task SendResetPWEmailAsync(string? toEmail, string subject, string basehref, string resetPasswordURL);
    }

    public class EmailService : IEmailService
    {
        public string _smtpServer;
        public int _smtpPort;
        public string _smtpUser;
        public string _smtpPassword;

        /// <summary>
        /// 建構函式，用於初始化 SMTP 設定
        /// </summary>
        public EmailService()
        {
            _smtpServer = "smtp.gmail.com";
            _smtpPort = 587;
            _smtpUser = "chinhsien437@gmail.com";
            _smtpPassword = "tohn hlaf xifp umdc";
        }

        /// <summary>
        /// 發送驗證郵件
        /// </summary>
        public async Task SendVerifyEmailAsync(string? toEmail, string subject, string basehref, string verifyCode)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("收件者 Email 不得為空");

            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Email 主題不得為空");

            string message = $@"
                    親愛的 申請人 您好：<br /><br />
                    感謝您申請CWT 命題工作平臺系統帳號。<br />
                    以下為您的信箱驗證碼，請於系統中輸入此驗證碼以完成認證：<br /><br />
                    請手動複製以下驗證碼：<br />
                    <strong>
                    <input value='{verifyCode}' readonly style='border:1px solid #ccc;padding:8px;font-size:20px;width:120px;text-align:center;' />
                    </strong><br /><br />
                    若有其他問題可再來電客服專線或來信客服服務信箱洽詢。<br /><br />
                    服務信箱：{_smtpUser}<br />
                    <a href='{basehref}'>CWT 命題工作平臺</a><br /><br />
                    ***本封信由系統自動寄出，請勿直接回覆!***<br /><br />";

            var smtpClient = new SmtpClient(_smtpServer)
            {
                Port = _smtpPort,
                Credentials = new NetworkCredential(_smtpUser, _smtpPassword),
                EnableSsl = true,
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_smtpUser, "CWT 命題工作平臺"),
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

        /// <summary>
        /// 發送密碼重設郵件
        /// </summary>
        public async Task SendResetPWEmailAsync(string? toEmail, string subject, string basehref, string resetPasswordURL)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("收件者 Email 不得為空");

            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Email 主題不得為空");

            string message = $@"
                    親愛的 老師 您好：<br /><br />
                    感謝您申請CWT 命題工作平臺系統帳號。請點選以下連結更新您的新密碼：<br /><br />
                    <strong><a href='{resetPasswordURL}'>點我更新密碼</a></strong><br /><br /><br />
                    如上述連結失效，請複製貼上前往下面連結：<br /><br /><br />
                    <strong><a href='{resetPasswordURL}'>{resetPasswordURL}</a></strong><br /><br /><br />
                    若有其他問題可再來電客服專線或來信客服服務信箱洽詢。<br /><br />
                    服務信箱：{_smtpUser}<br />
                    <a href='{basehref}'>CWT 命題工作平臺</a><br /><br />
                    ***本封信由系統自動寄出，請勿直接回覆!***<br /><br />";

            var smtpClient = new SmtpClient(_smtpServer)
            {
                Port = _smtpPort,
                Credentials = new NetworkCredential(_smtpUser, _smtpPassword),
                EnableSsl = true,
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_smtpUser, "CWT 命題工作平臺"),
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
