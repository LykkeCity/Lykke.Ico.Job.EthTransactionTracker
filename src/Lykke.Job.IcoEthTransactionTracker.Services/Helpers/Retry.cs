using System;
using System.Threading.Tasks;

namespace Lykke.Job.IcoEthTransactionTracker.Services.Helpers
{
    public static class Retry
    {
        /// <summary>
        /// Retries action and returns result or throws exception if count of tries exceeded specified number
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">Action</param>
        /// <param name="retryOnException">Exception filter, should return true to retry or false to throw immediately</param>
        /// <param name="tryCount">Number of tries</param>
        /// <param name="delayAfterException">Delay before retry, in milliseconds</param>
        /// <returns></returns>
        public static async Task<T> Try<T>(
            Func<Task<T>> action, 
            Func<Exception, bool> retryOnException,
            int tryCount,
            int delayAfterException = 0)
        {
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    // if there is no more tries then exception type doesn't matter
                    if (--tryCount == 0 || !retryOnException(ex)) 
                    {
                        throw;
                    }

                    // is it useful to retry immediately?
                    if (delayAfterException > 0)
                    {
                        await Task.Delay(delayAfterException);
                    }
                }
            }
        }
    }
}
