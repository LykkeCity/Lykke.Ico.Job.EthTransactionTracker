using System;
using Nethereum.RPC.Eth.DTOs;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain
{
    public class BlockInformation
    {
        public BlockInformation()
        {
        }

        public BlockInformation(BlockWithTransactionHashes block)
        {
            BlockId = block.BlockHash;
            Height = (ulong)block.Number.Value;
            Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)block.Timestamp.Value);
            IsEmpty = block.TransactionHashes.Length == 0;
        }

        public string BlockId { get; set; }
        public ulong Height { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public bool IsEmpty { get; set; }
    }
}
