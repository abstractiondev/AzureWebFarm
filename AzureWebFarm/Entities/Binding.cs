using System;

namespace AzureWebFarm.Entities
{
    public class Binding
    {
        private string _ipAddress;

        public Binding()
            : this(Guid.NewGuid())
        {
        }

        public Binding(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; private set; }

        public Guid WebSiteId { get; set; }

        public WebSite WebSite { get; set; }

        public string Protocol { get; set; }

        public string IpAddress
        {
            get { return String.IsNullOrWhiteSpace(_ipAddress) ? "*" : _ipAddress; }

            set { _ipAddress = value; }
        }

        public int Port { get; set; }

        public string HostName { get; set; }

        public string CertificateThumbprint { get; set; }

        public string BindingInformation
        {
            get { return string.Format("{0}:{1}:{2}", IpAddress, Port, HostName); }
        }
    }
}