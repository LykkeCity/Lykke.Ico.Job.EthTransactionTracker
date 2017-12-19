using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class BlockchainReader : IBlockchainReader
    {
        private readonly Web3 _web3;
        private readonly bool _useTraceFilter;

        public BlockchainReader(string ethereumUrl, bool useTraceFilter = true)
        {
            _web3 = new Web3(ethereumUrl);
            _useTraceFilter = useTraceFilter;
        }

        public async Task<ulong> GetLastConfirmedHeightAsync(ulong confirmationLimit)
        {
            return (ulong)(await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value - confirmationLimit;
        }

        public async Task<BlockInformation> GetBlockByHeightAsync(ulong height)
        {
            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger(height));
            if (block != null)
                return new BlockInformation(block);
            else
                return null;
        }

        public async Task<BlockInformation> GetBlockByIdAsync(string id)
        {
            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsHashesByHash.SendRequestAsync(id);
            if (block != null)
                return new BlockInformation(block);
            else
                return null;
        }

        public async Task<TransactionTrace[]> GetBlockTransactionsAsync(ulong height, bool paymentsOnly = true)
        {
            var blockHeight = new HexBigInteger(height);
            var traceParams = new { fromBlock = blockHeight, toBlock = blockHeight };
            var txs = new List<TransactionTrace>();

            if (_useTraceFilter)
            {
                // use traces instead of regular data to get all txs including inner transactions
                txs.AddRange(await _web3.Client.SendRequestAsync<TransactionTrace[]>("trace_filter", null, traceParams));
            }
            else
            {
                // get transactions without inner transactions
                var block = await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(height));
                foreach (var tx in block.Transactions)
                {
                    txs.Add(new TransactionTrace
                    {
                        Action = new TransactionTraceAction { To = tx.To, From = tx.From, Value = tx.Value },
                        BlockHash = tx.BlockHash,
                        TransactionHash = tx.TransactionHash
                    });
                }
            }

            if (paymentsOnly)
            {
                txs = txs
                    .Where(t => !string.IsNullOrWhiteSpace(t.Action?.To) && t.Action?.Value?.Value > 0)
                    .ToList();
            }

            return txs.ToArray();
        }
    }
}
