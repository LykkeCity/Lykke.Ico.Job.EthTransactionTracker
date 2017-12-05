using System;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class BlockchainReader : IBlockchainReader
    {
        private readonly Web3 _web3;

        public BlockchainReader(string ethereumUrl)
        {
            var client = new RpcClient(new Uri(ethereumUrl));

            _web3 = new Web3(client);
        }

        public async Task<UInt64> GetLastConfirmedHeightAsync(UInt64 confirmationLimit)
        {
            return (UInt64)(await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value - confirmationLimit;
        }

        public async Task<TransactionTrace[]> GetBlockTransactionsAsync(UInt64 height, bool paymentsOnly = true)
        {
            var blockHeight = new HexBigInteger(height);
            var traceParams = new { fromBlock = blockHeight, toBlock = blockHeight };

            // we need to use traces instead of regular data to get all txs including inner transactions
            var txs = await _web3.Client.SendRequestAsync<TransactionTrace[]>("trace_filter", null, traceParams);

            if (paymentsOnly)
            {
                txs = txs
                    .Where(t => !string.IsNullOrWhiteSpace(t.Action?.To) && t.Action?.Value?.Value > 0)
                    .ToArray();
            }

            return txs;
        }

        public async Task<(DateTimeOffset Timestamp, Boolean IsEmpty)> GetBlockInfoAsync(UInt64 height)
        {
            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger(height));
            var timestamp = DateTimeOffset.FromUnixTimeSeconds((Int64)block.Timestamp.Value);
            var isEmpty = block.TransactionHashes.Length == 0;

            return (timestamp, isEmpty);
        }
    }
}
