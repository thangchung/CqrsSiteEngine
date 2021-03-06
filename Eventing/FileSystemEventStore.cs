﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ncqrs.Eventing;
using Ncqrs.Eventing.Sourcing;
using Ncqrs.Eventing.Storage;

namespace Eventing
{
    public class FileSystemEventStore : IExplorableEventStore
    {
        public string BasePath { get; protected set; }

        public IEventStreamer Streamer { get; private set; }

        public FileSystemEventStore(string basePath)
        {
            BasePath = basePath;

            Streamer = new EventStreamer(new EventSerializer(MessagesProvider.GetKnownEventTypes()));
        }

        #region EventStore

        public CommittedEventStream ReadFrom(Guid id, long minVersion, long maxVersion)
        {
            List<CommittedEvent> events = GetAllEventsSinceVersion(id, minVersion)
                .Where(n => n.EventSequence <= maxVersion)
                .Select(evt => new CommittedEvent(
                    Guid.NewGuid(), evt.EventIdentifier, id, evt.EventSequence,
                    evt.EventTimeStamp, evt, evt.EventVersion))
                .ToList();

            var stream = new CommittedEventStream(id, events);

            return stream;
        }

        public void Store(UncommittedEventStream eventStream)
        {
            var sourcePath = string.Format("{0}{1}{2}", BasePath, Path.DirectorySeparatorChar, eventStream.SourceId);

            if (!Directory.Exists(BasePath))
            {
                Directory.CreateDirectory(BasePath);
            }

            var tapeStream = new FileTapeStream.FileTapeStream(sourcePath);

            foreach (var record in eventStream.Select(evt => Streamer.SerializeEvent(evt.Payload as ISourcedEvent)))
            {
                tapeStream.Append(record);
            }
        }

        public void StoreEmptyEventSource(Guid id)
        {
            var sourcePath = string.Format("{0}{1}{2}", BasePath, Path.DirectorySeparatorChar, id);
            if (File.Exists(sourcePath))
            {
                return;
            }

            using (FileStream fs = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                fs.Flush();
            }
        }

        public void RemoveEmptyEventSource(Guid id)
        {
            var sourcePath = string.Format("{0}{1}{2}", BasePath, Path.DirectorySeparatorChar, id);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            var events = GetAllEvents(id);

            if (!events.Any())
            {
                File.Delete(sourcePath);
            }
        }

        public IEnumerable<ISourcedEvent> GetAllEvents(Guid id)
        {

            var sourcePath = string.Format("{0}{1}{2}", BasePath, Path.DirectorySeparatorChar, id);

            var tapeStream = new FileTapeStream.FileTapeStream(sourcePath);
            var records = tapeStream.ReadRecords();

            List<ISourcedEvent> events = records.Select(rec => Streamer.DeserializeEvent(rec.Data)).ToList();

            // todo: Add caching

            return events.OrderBy(n => n.EventTimeStamp);
        }

        public IEnumerable<ISourcedEvent> GetAllEventsSinceVersion(Guid id, long version)
        {
            var all = GetAllEvents(id);

            return all.Where(n => n.EventSequence >= version);

            // todo: optimize this
        }

        #endregion

        public IEnumerable<Guid> GetEventSourceIndex()
        {
            List<FileInfo> fileInfos = Directory
                .GetFiles(BasePath)
                .Select(id => new FileInfo(id))
                .ToList();

            fileInfos = fileInfos.OrderBy(n => n.LastWriteTimeUtc).ToList();

            var index = new List<Guid>();

            foreach (var file in fileInfos)
            {
                string id = file.Name;
                Guid guid_id = Guid.Empty;
                if (!Guid.TryParse(id, out guid_id))
                {
                    continue;
                }
                index.Add(guid_id);
            }
            return index;
        }

        protected string GetSourcePath(IEventSource source)
        {
            return string.Format("{0}{1}{2}", BasePath, Path.DirectorySeparatorChar, source.EventSourceId);
        }
    }
}
