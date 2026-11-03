// Stand-ins for the transfer area members of Siemens.Engineering.Base.xml (V21): NetworkInterface.TransferAreas and
// .MulticastableTransferAreas with their documented Create overloads, the areas' documented properties and Delete(). Member shapes and
// a simple list model only; no evidence of Siemens behavior.
namespace Siemens.Engineering.HW
{
    public enum TransferAreaType { None, CD, DDX, F_CD, IN, OUT, IN_OUT, MSI, MSO, MS }

    public enum TransferAreaDirection { None, LocalToPartner, PartnerToLocal, Bidirectional }

    public sealed class TransferArea(TransferAreaComposition owner, string name, TransferAreaType type, int position)
    {
        public string Name { get; set; } = name;
        public TransferAreaType Type { get; } = type;
        public TransferAreaDirection Direction { get; set; } = TransferAreaDirection.LocalToPartner;
        public int PositionNumber { get; } = position;
        public int ExtendedPositionNumber => 0;
        public int LocalToPartnerLength { get; set; } = 4;
        public int PartnerToLocalLength { get; set; }
        public List<object> LocalAddresses { get; } = new() { new object() };
        public List<object> PartnerAddresses { get; } = new();
        public List<object> TransferAreaMappingRules { get; } = new();

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class TransferAreaComposition : IEnumerable<TransferArea>
    {
        internal List<TransferArea> Items { get; } = new();

        public TransferArea Create(string name, TransferAreaType type) => Create(name, type, 100 + Items.Count);

        public TransferArea Create(string name, TransferAreaType type, int positionNumber)
        {
            if (Items.Any(item => item.Name == name)) throw new InvalidOperationException($"A transfer area named '{name}' already exists.");
            var area = new TransferArea(this, name, type, positionNumber);
            Items.Add(area);
            return area;
        }

        public IEnumerator<TransferArea> GetEnumerator() => Items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class MulticastableTransferArea(MulticastableTransferAreaComposition owner, string name, TransferAreaType type, int length)
    {
        public string Name { get; set; } = name;
        public TransferAreaType Type { get; } = type;
        public TransferAreaDirection Direction { get; set; } = TransferAreaDirection.LocalToPartner;
        public int DataLength { get; set; } = length;
        public string Comment { get; set; } = string.Empty;
        public List<object> Addresses { get; } = new() { new object() };
        public List<MulticastableTransferArea> PartnerTransferAreas { get; } = new();

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class MulticastableTransferAreaComposition : IEnumerable<MulticastableTransferArea>
    {
        internal List<MulticastableTransferArea> Items { get; } = new();

        public MulticastableTransferArea Create(TiaMcpServer.FakeWorker.Network.FakeNetworkInterface partner, TransferAreaType type)
            => Create(partner, type, $"TransferArea_{Items.Count + 1}", 1);

        public MulticastableTransferArea Create(TiaMcpServer.FakeWorker.Network.FakeNetworkInterface partner, TransferAreaType type, string name)
            => Create(partner, type, name, 1);

        public MulticastableTransferArea Create(TiaMcpServer.FakeWorker.Network.FakeNetworkInterface partner, TransferAreaType type, string name, int length)
        {
            var area = new MulticastableTransferArea(this, name, type, length);
            Items.Add(area);
            return area;
        }

        public MulticastableTransferArea Create(MulticastableTransferArea template, TransferAreaType type)
        {
            var area = new MulticastableTransferArea(this, template.Name, type, template.DataLength);
            area.PartnerTransferAreas.Add(template);
            Items.Add(area);
            return area;
        }

        public IEnumerator<MulticastableTransferArea> GetEnumerator() => Items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace TiaMcpServer.FakeWorker.Network
{
    /// <summary>The NetworkInterface stand-in of an interface item: the two transfer area compositions and one IO connector state.</summary>
    public sealed class FakeNetworkInterface
    {
        public Siemens.Engineering.HW.TransferAreaComposition TransferAreas { get; } = new();

        public Siemens.Engineering.HW.MulticastableTransferAreaComposition MulticastableTransferAreas { get; } = new();

        /// <summary>The IO system number the interface's single IO connector is connected to; null when disconnected.</summary>
        public int? ConnectedIoSystem { get; set; }
    }
}
