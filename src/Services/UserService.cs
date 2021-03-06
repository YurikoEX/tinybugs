﻿namespace RobMensching.TinyBugs.Services
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using RobMensching.TinyBugs.Models;
    using ServiceStack.OrmLite;

    public static class UserService
    {
        private const int PasswordHashIterations = 20000;
        private const string TokenIssuedFormat = "yyyyMMddHHmm";

        public static User Create(string email, string password = null, string username = null)
        {
            byte[] salt = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            Guid id = Guid.NewGuid();
            username = String.IsNullOrEmpty(username) ? email.ToLowerInvariant() : username.ToLowerInvariant();

            return new User()
            {
                Guid = id,
                Email = email.ToLowerInvariant(),
                UserName = username,
                GravatarId = GenerateGravatarId(email),
                Salt = Convert.ToBase64String(salt),
                PasswordHash = CalculatePasswordHash(id, salt, password),
            };
        }

        public static string CalculatePasswordHash(Guid id, string salt, string password)
        {
            byte[] saltBytes = Convert.FromBase64String(salt);
            return CalculatePasswordHash(id, saltBytes, password);
        }

        public static string CalculatePasswordHash(Guid id, byte[] salt, string password)
        {
            byte[] passwordBytes;
            using (SHA256 sha = SHA256.Create())
            {
                string namePassword = id.ToString("N") + password;
                passwordBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(namePassword));
            }

            using (Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, salt, PasswordHashIterations))
            {
                byte[] bytes = pbkdf2.GetBytes(64);
                return Convert.ToBase64String(bytes);
            }
        }

        public static string GenerateGravatarId(string email)
        {
            byte[] emailBytes = Encoding.ASCII.GetBytes(email.Trim().ToLowerInvariant());
            using (MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider())
            {
                byte[] idBytes = md5.ComputeHash(emailBytes);
                return BytesToHex(idBytes);
            }
        }

        public static string GenerateVerifyToken()
        {
            byte[] salt = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            return BytesToHex(salt) + DateTime.UtcNow.ToString(TokenIssuedFormat);
        }

        public static string GetGravatarImageUrl(string id, bool secure = false, int size = 0)
        {
            string protocol = secure ? "https" : "http";
            string sizeQuery = size > 0 ? "&s=" + size : String.Empty;
            return String.Format("{0}://www.gravatar.com/avatar/{1}?r=pg&d=mm{2}", protocol, id, sizeQuery);
        }

        public static string GetGravatarImageUrlForEmail(string email, bool secure = false, int size = 0)
        {
            string id = GenerateGravatarId(email);
            string protocol = secure ? "https" : "http";
            string sizeQuery = size > 0 ? "&s=" + size : String.Empty;
            return String.Format("{0}://www.gravatar.com/avatar/{1}?r=pg&d=mm{2}", protocol, id, sizeQuery);
        }

        public static bool TryAuthenticateUser(string username, string password, out User user)
        {
            username = String.IsNullOrEmpty(username) ? String.Empty : username.ToLowerInvariant();

            if (!QueryService.TryGetUser(username, out user))
            {
                return false;
            }

            string hash = UserService.CalculatePasswordHash(user.Guid, user.Salt, password);
            return user.PasswordHash.Equals(hash, StringComparison.Ordinal);
        }

        public static bool TryAuthenticateUser(Guid userGuid, out User user)
        {
            using (var db = DataService.Connect(true))
            {
                user = db.FirstOrDefault<User>(u => u.Guid == userGuid);
            }

            return user != null;
        }

        public static bool TryAuthorizeUser(User user, UserRole role)
        {
            return user.IsInRole(role);
        }

        public static bool TryValidateVerificationToken(string token, out DateTime issued)
        {
            issued = DateTime.MinValue;

            return (!String.IsNullOrEmpty(token) &&
                    DateTime.TryParseExact(token.Substring(token.Length - TokenIssuedFormat.Length), TokenIssuedFormat, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out issued) &&
                    DateTime.UtcNow < issued.AddMinutes(60));
        }

        private static string BytesToHex(byte[] bytes)
        {
            const string hexAlphabet = "0123456789abcdef";

            StringBuilder sb = new StringBuilder(bytes.Length);
            foreach (byte b in bytes)
            {
                sb.Append(hexAlphabet[(int)(b >> 4)]);
                sb.Append(hexAlphabet[(int)(b & 0xF)]);
            }

            return sb.ToString();
        }
    }
}
