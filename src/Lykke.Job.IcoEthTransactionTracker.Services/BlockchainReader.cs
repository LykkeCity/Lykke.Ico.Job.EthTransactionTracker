using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using Polly;
using Polly.Retry;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class BlockchainReader : IBlockchainReader
    {
        private readonly Web3 _web3;
        private readonly bool _useTraceFilter;
        private readonly RetryPolicy _retry;

        public BlockchainReader(string ethereumUrl, bool useTraceFilter = true)
        {
            _web3 = new Web3(ethereumUrl);
            _useTraceFilter = useTraceFilter;
            // 10 times by 50 ms were figured out by several adjustments
            _retry = Policy
                .Handle<RpcClientUnknownException>()
                .WaitAndRetryAsync(10, _ => TimeSpan.FromMilliseconds(50));
        }

        public async Task<ulong> GetLastConfirmedHeightAsync(ulong confirmationLimit)
        {
            return (ulong)(await _retry.ExecuteAsync(() => _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync())).Value - confirmationLimit;
        }

        public async Task<BlockInformation> GetBlockByHeightAsync(ulong height)
        {
            var block = await _retry.ExecuteAsync(() => _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger(height)));
            if (block != null)
                return new BlockInformation(block);
            else
                return null;
        }

        public async Task<BlockInformation> GetBlockByIdAsync(string id)
        {
            var block = await _retry.ExecuteAsync(() => _web3.Eth.Blocks.GetBlockWithTransactionsHashesByHash.SendRequestAsync(id));
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
                txs.AddRange(await _retry.ExecuteAsync(() => _web3.Client.SendRequestAsync<TransactionTrace[]>("trace_filter", null, traceParams)));
            }
            else
            {
                // get transactions without inner transactions
                var block = await _retry.ExecuteAsync(() => _web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(height)));
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
