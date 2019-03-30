﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeetleX.Redis
{
    public class RedisDB
    {
        public RedisDB(int db = 0, IDataFormater dataFormater = null)
        {
            DB = db;
            DataFormater = dataFormater;
            mDetectionTime = new System.Threading.Timer(OnDetection, null, 1000, 1000);
        }

        private System.Threading.Timer mDetectionTime;

        private void OnDetection(object state)
        {
            mDetectionTime.Change(-1, -1);

            var wHosts = mWriteActives;
            foreach (var item in wHosts)
                item.Ping();
            var rHost = mReadActives;
            foreach (var item in rHost)
                item.Ping();
            mDetectionTime.Change(1000, 1000);

        }

        public IDataFormater DataFormater { get; set; }

        private List<RedisHost> mWriteHosts = new List<RedisHost>();

        private List<RedisHost> mReadHosts = new List<RedisHost>();

        private RedisHost[] mWriteActives = new RedisHost[0];

        private RedisHost[] mReadActives = new RedisHost[0];

        private bool OnClientPush(RedisClient client)
        {
            return true;
        }

        public int DB { get; set; }

        public RedisHost AddWriteHost(string host, int port = 6379)
        {
            RedisHost redisHost = new RedisHost(DB, host, port);
            mWriteHosts.Add(redisHost);
            mWriteActives = mWriteHosts.ToArray();
            return redisHost;
        }

        public RedisHost AddReadHost(string host, int port = 6379)
        {
            RedisHost redisHost = new RedisHost(DB, host, port);
            mReadHosts.Add(redisHost);
            mReadActives = mReadHosts.ToArray();
            return redisHost;
        }

        public RedisHost GetWriteHost()
        {
            var items = mWriteActives;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Available)
                    return items[i];
            }
            return null;
        }

        public RedisHost GetReadHost()
        {
            var items = mReadActives;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Available)
                    return items[i];
            }
            return GetWriteHost();
        }

        public Subscriber Subscribe()
        {
            Subscriber result = new Subscriber(this);
            return result;
        }

        public async Task<Result> Execute(Command cmd, params Type[] types)
        {
            var host = cmd.Read ? GetReadHost() : GetWriteHost();
            if (host == null)
            {
                return new Result() { ResultType = ResultType.NetError, Messge = "redis server is not available" };
            }
            var client = await host.Pop();
            if (client == null)
                return new Result() { ResultType = ResultType.NetError, Messge = "exceeding maximum number of connections" };
            var result = host.Connect(client);
            if (result.IsError)
            {
                host.Push(client);
                return result;
            }
            RedisRequest request = new RedisRequest(host, client, cmd, types);
            result = await request.Execute();
            return result;

        }

        public async ValueTask<string> Flushall()
        {
            Commands.FLUSHALL cmd = new Commands.FLUSHALL();
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<bool> Ping()
        {
            Commands.PING ping = new Commands.PING(null);
            var result = await Execute(ping, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return true;
        }

        public async ValueTask<long> Del(params string[] key)
        {
            Commands.DEL del = new Commands.DEL(key);
            var result = await Execute(del, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<string> Set(string key, object value)
        {
            return await Set(key, value, null, null);
        }

        public async ValueTask<string> Set(string key, object value, int? seconds)
        {
            return await Set(key, value, seconds, null);
        }

        public async ValueTask<string> Set(string key, object value, int? seconds, bool? nx)
        {
            Commands.SET set = new Commands.SET(key, value, DataFormater);
            if (seconds != null)
            {
                set.ExpireTimeType = ExpireTimeType.EX;
                set.TimeOut = seconds.Value;
            }
            set.NX = nx;
            var result = await Execute(set, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;

        }

        public async ValueTask<string> Dump(string key)
        {
            Commands.DUMP dump = new Commands.DUMP(key);
            var result = await Execute(dump, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<long> Exists(params string[] key)
        {
            Commands.EXISTS exists = new Commands.EXISTS(key);
            var result = await Execute(exists, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Expire(string key, int seconds)
        {
            Commands.EXPIRE expire = new Commands.EXPIRE(key, seconds);
            var result = await Execute(expire, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Ttl(string key)
        {
            Commands.TTL cmd = new Commands.TTL(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> PTtl(string key)
        {
            Commands.PTTL cmd = new Commands.PTTL(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Expireat(string key, long timestamp)
        {
            Commands.EXPIREAT cmd = new Commands.EXPIREAT(key, timestamp);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<string> MSet(Func<Commands.MSET, Commands.MSET> handler)
        {
            Commands.MSET cmd = new Commands.MSET(DataFormater);
            handler(cmd);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<long> MSetNX(Func<Commands.MSETNX, Commands.MSETNX> handler)
        {
            Commands.MSETNX cmd = new Commands.MSETNX(DataFormater);
            handler(cmd);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<T> Get<T>(string key)
        {
            Commands.GET cmd = new Commands.GET(key, DataFormater);
            var result = await Execute(cmd, typeof(T));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (T)result.Value;
        }

        public async ValueTask<string[]> Keys(string pattern)
        {
            Commands.KEYS cmd = new Commands.KEYS(pattern);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (from a in result.Data select (string)a.Data).ToArray();
        }

        public async ValueTask<long> Move(string key, int db)
        {
            Commands.MOVE cmd = new Commands.MOVE(key, db);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Persist(string key)
        {
            Commands.PERSIST cmd = new Commands.PERSIST(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Pexpire(string key, long milliseconds)
        {
            Commands.PEXPIRE cmd = new Commands.PEXPIRE(key, milliseconds);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Pexpireat(string key, long timestamp)
        {
            Commands.PEXPIREAT cmd = new Commands.PEXPIREAT(key, timestamp);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<string> Randomkey()
        {
            Commands.RANDOMKEY cmd = new Commands.RANDOMKEY();
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<string> Rename(string key, string newkey)
        {
            Commands.RENAME cmd = new Commands.RENAME(key, newkey);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<long> Renamenx(string key, string newkey)
        {
            Commands.RENAMENX cmd = new Commands.RENAMENX(key, newkey);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Touch(params string[] keys)
        {
            Commands.TOUCH cmd = new Commands.TOUCH(keys);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<string> Type(string key)
        {
            Commands.TYPE cmd = new Commands.TYPE(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<long> Unlink(params string[] keys)
        {
            Commands.UNLINK cmd = new Commands.UNLINK(keys);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Decr(string key)
        {
            Commands.DECR cmd = new Commands.DECR(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Decrby(string key, int decrement)
        {
            Commands.DECRBY cmd = new Commands.DECRBY(key, decrement);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> SetBit(string key, int offset, bool value)
        {
            Commands.SETBIT cmd = new Commands.SETBIT(key, offset, value);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> GetBit(string key, int offset)
        {
            Commands.GETBIT cmd = new Commands.GETBIT(key, offset);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<string> GetRange(string key, int start, int end)
        {
            Commands.GETRANGE cmd = new Commands.GETRANGE(key, start, end);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<T> GetSet<T>(string key, object value)
        {
            Commands.GETSET cmd = new Commands.GETSET(key, value, DataFormater);
            var result = await Execute(cmd, typeof(T));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (T)result.Value;
        }

        public async ValueTask<long> Incr(string key)
        {
            Commands.INCR cmd = new Commands.INCR(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Incrby(string key, int increment)
        {
            Commands.INCRBY cmd = new Commands.INCRBY(key, increment);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<float> IncrbyFloat(string key, float increment)
        {
            Commands.INCRBYFLOAT cmd = new Commands.INCRBYFLOAT(key, increment);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return float.Parse((string)result.Value);
        }

        public async ValueTask<(T, T1)> MGet<T, T1>(string key1, string key2)
        {
            string[] keys = { key1, key2 };
            Type[] types = { typeof(T), typeof(T1) };
            var items = await MGet(keys, types);
            return ((T)items[0], (T1)items[1]);
        }

        public async ValueTask<(T, T1, T2)> MGet<T, T1, T2>(string key1, string key2, string key3)
        {
            string[] keys = { key1, key2, key3 };
            Type[] types = { typeof(T), typeof(T1), typeof(T2) };
            var items = await MGet(keys, types);
            return ((T)items[0], (T1)items[1], (T2)items[2]);
        }

        public async ValueTask<(T, T1, T2, T3)> MGet<T, T1, T2, T3>(string key1, string key2, string key3, string key4)
        {
            string[] keys = { key1, key2, key3, key4 };
            Type[] types = { typeof(T), typeof(T1), typeof(T2), typeof(T3) };
            var items = await MGet(keys, types);
            return ((T)items[0], (T1)items[1], (T2)items[2], (T3)items[3]);
        }

        public async ValueTask<(T, T1, T2, T3, T4)> MGet<T, T1, T2, T3, T4>(string key1, string key2, string key3, string key4, string key5)
        {
            string[] keys = { key1, key2, key3, key4, key5 };
            Type[] types = { typeof(T), typeof(T1), typeof(T2), typeof(T3), typeof(T4) };
            var items = await MGet(keys, types);
            return ((T)items[0], (T1)items[1], (T2)items[2], (T3)items[3], (T4)items[4]);
        }

        public async ValueTask<object[]> MGet(string[] keys, Type[] types)
        {
            Commands.MGET cmd = new Commands.MGET(DataFormater, keys);
            var result = await Execute(cmd, types);
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (from a in result.Data select a.Data).ToArray();

        }

        public async ValueTask<string> PSetEX(string key, long milliseconds, object value)
        {
            Commands.PSETEX cmd = new Commands.PSETEX(key, milliseconds, value, DataFormater);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<string> SetEX(string key, int seconds, object value)
        {
            Commands.SETEX cmd = new Commands.SETEX(key, seconds, value, DataFormater);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (string)result.Value;
        }

        public async ValueTask<long> SetNX(string key, object value)
        {
            Commands.SETNX cmd = new Commands.SETNX(key, value, DataFormater);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> SetRange(string key, int offset, string value)
        {
            Commands.SETRANGE cmd = new Commands.SETRANGE(key, offset, value);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public async ValueTask<long> Strlen(string key)
        {
            Commands.STRLEN cmd = new Commands.STRLEN(key);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }

        public RedisHashTable CreateHashTable(string key)
        {
            return new RedisHashTable(this, key, DataFormater);
        }

        public RedisList<T> CreateList<T>(string key)
        {
            return new RedisList<T>(this, key, DataFormater);
        }

        public async ValueTask<long> Publish(string channel, object data)
        {
            Commands.PUBLISH cmd = new Commands.PUBLISH(channel, data, DataFormater);
            var result = await Execute(cmd, typeof(string));
            if (result.IsError)
                throw new RedisException(result.Messge);
            return (long)result.Value;
        }
    }
}
