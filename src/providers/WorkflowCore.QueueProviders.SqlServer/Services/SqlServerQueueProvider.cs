﻿#region using

using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using WorkflowCore.Interface;

#endregion

namespace WorkflowCore.QueueProviders.SqlServer.Services
{
    public class SqlServerQueueProvider : IQueueProvider
    {
        readonly string _connectionString;

        readonly bool _canMigrateDb;
        readonly bool _canCreateDb;

        private readonly IBrokerNamesProvider _names;
        private readonly ISqlServerQueueProviderMigrator _migrator;
        private readonly ISqlCommandExecutor _sqlCommandExecutor;

        private readonly string _queueWork;
        private readonly string _dequeueWork;

        public SqlServerQueueProvider(SqlServerQueueProviderOption opt, IBrokerNamesProvider names, ISqlServerQueueProviderMigrator migrator, ISqlCommandExecutor sqlCommandExecutor)
        {
            _names = names;
            _migrator = migrator;
            _sqlCommandExecutor = sqlCommandExecutor;
            _connectionString = opt.ConnectionString;
            _canMigrateDb = opt.CanMigrateDb;
            _canCreateDb = opt.CanCreateDb;

            IsDequeueBlocking = true;

            _queueWork = GetFromResource("QueueWork");
            _dequeueWork = GetFromResource("DequeueWork");
        }

        private static string GetFromResource(string file)
        {
            var resName = $"WorkflowCore.QueueProviders.SqlServer.Services.{file}.sql";

            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(resName)))
            {
                return reader.ReadToEnd();
            }
        }


        public bool IsDequeueBlocking { get; }

#pragma warning disable CS1998

        public async Task Start()
        {
            if (_canCreateDb) _migrator.CreateDb();
            if (_canMigrateDb) _migrator.MigrateDb();
        }

        public async Task Stop()
        {
            // Do nothing
        }

#pragma warning restore CS1998

        public void Dispose()
        {
            Stop().Wait();
        }

        /// <inheritdoc />
        /// <summary>
        /// Write a new id to the specified queue
        /// </summary>
        /// <param name="id"></param>
        /// <param name="queue"></param>
        /// <returns></returns>
        public async Task QueueWork(string id, QueueType queue)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id), "Param id must not be null");

            SqlConnection cn = null;
            try
            {
                var par = _names.GetByQueue(queue);
                
                cn = new SqlConnection(_connectionString);
                cn.Open();
                using (var cmd = _sqlCommandExecutor.CreateCommand(cn, null, _queueWork))
                {
                    cmd.Parameters.AddWithValue("@initiatorService", par.InitiatorService);
                    cmd.Parameters.AddWithValue("@targetService", par.TargetService);
                    cmd.Parameters.AddWithValue("@contractName", par.ContractName);
                    cmd.Parameters.AddWithValue("@msgType", par.MsgType);
                    cmd.Parameters.AddWithValue("@RequestMessage", id);
                    await cmd.ExecuteNonQueryAsync();
                }
            } finally
            {
                cn?.Close();
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Get an id from the specified queue.
        /// </summary>
        /// <param name="queue"></param>
        /// <param name="cancellationToken">cancellationToken</param>
        /// <returns>Next id from queue, null if no message arrives in one second.</returns>
        public async Task<string> DequeueWork(QueueType queue, CancellationToken cancellationToken)
        {
            SqlConnection cn = null;
            try
            {
                var par = _names.GetByQueue(queue);
                
                var sql = _dequeueWork.Replace("{queueName}", par.QueueName);

                cn = new SqlConnection(_connectionString);
                cn.Open();
                using (var cmd = _sqlCommandExecutor.CreateCommand(cn, null, sql))
                {
                    var msg = await cmd.ExecuteScalarAsync(cancellationToken);
                    return msg is DBNull ? null : (string)msg;
                }
            } finally
            {
                cn?.Close();
            }
        }
    }
}