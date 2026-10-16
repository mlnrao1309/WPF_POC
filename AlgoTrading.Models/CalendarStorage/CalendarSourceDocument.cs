using System;
using System.Collections.Generic;

namespace AlgoTrading.Models
{
    public sealed class CalendarSourceDocument
    {
        private readonly Guid sourceId;
        private readonly string exchangeCode;
        private readonly Uri sourceUri;
        private readonly DateTimeOffset retrievedAt;
        private readonly string contentType;
        private readonly string content;

        public CalendarSourceDocument(Guid sourceId, string exchangeCode, Uri sourceUri, DateTimeOffset retrievedAt, string contentType, string content)
        {
            if (sourceId == Guid.Empty || string.IsNullOrWhiteSpace(exchangeCode) || exchangeCode.Length > 16)
            {
                throw new ArgumentException("A source identity and exchange code are required.");
            }
            if (sourceUri == null || !sourceUri.IsAbsoluteUri || sourceUri.Scheme != Uri.UriSchemeHttps || sourceUri.AbsoluteUri.Length > 2048)
            {
                throw new ArgumentException("An absolute HTTPS source URI is required.");
            }
            if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || string.IsNullOrWhiteSpace(content) || content.Length > 8000000)
            {
                throw new ArgumentException("Valid bounded source content and media type are required.");
            }
            this.sourceId = sourceId;
            this.exchangeCode = exchangeCode;
            this.sourceUri = sourceUri;
            this.retrievedAt = retrievedAt;
            this.contentType = contentType;
            this.content = content;
        }

        public Guid SourceId
        {
            get
            {
                return this.sourceId;
            }
        }

        public string ExchangeCode
        {
            get
            {
                return this.exchangeCode;
            }
        }

        public Uri SourceUri
        {
            get
            {
                return this.sourceUri;
            }
        }

        public DateTimeOffset RetrievedAt
        {
            get
            {
                return this.retrievedAt;
            }
        }

        public string ContentType
        {
            get
            {
                return this.contentType;
            }
        }

        public string Content
        {
            get
            {
                return this.content;
            }
        }

        public string GetContentSha256()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(this.content);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
    }
}
