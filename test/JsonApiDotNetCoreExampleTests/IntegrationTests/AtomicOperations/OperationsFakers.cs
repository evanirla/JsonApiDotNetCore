using System;
using Bogus;

namespace JsonApiDotNetCoreExampleTests.IntegrationTests.AtomicOperations
{
    internal sealed class OperationsFakers : FakerContainer
    {
        private readonly Lazy<Faker<Playlist>> _lazyPlaylistFaker = new Lazy<Faker<Playlist>>(() =>
            new Faker<Playlist>()
                .UseSeed(GetFakerSeed())
                .RuleFor(playlist => playlist.Name, f => f.Lorem.Sentence()));

        private readonly Lazy<Faker<MusicTrack>> _lazyMusicTrackFaker = new Lazy<Faker<MusicTrack>>(() =>
            new Faker<MusicTrack>()
                .UseSeed(GetFakerSeed())
                .RuleFor(musicTrack => musicTrack.Title, f => f.Lorem.Word())
                .RuleFor(musicTrack => musicTrack.LengthInSeconds, f => f.Random.Decimal(3 * 60, 5 * 60))
                .RuleFor(musicTrack => musicTrack.Genre, f => f.Lorem.Word())
                .RuleFor(musicTrack => musicTrack.ReleasedAt, f => f.Date.PastOffset()));

        private readonly Lazy<Faker<Performer>> _lazyPerformerFaker = new Lazy<Faker<Performer>>(() =>
            new Faker<Performer>()
                .UseSeed(GetFakerSeed())
                .RuleFor(performer => performer.ArtistName, f => f.Name.FullName())
                .RuleFor(performer => performer.BornAt, f => f.Date.PastOffset()));

        private readonly Lazy<Faker<RecordCompany>> _lazyRecordCompanyFaker = new Lazy<Faker<RecordCompany>>(() =>
            new Faker<RecordCompany>()
                .UseSeed(GetFakerSeed())
                .RuleFor(recordCompany => recordCompany.Name, f => f.Company.CompanyName())
                .RuleFor(recordCompany => recordCompany.CountryOfResidence, f => f.Address.Country()));

        public Faker<Playlist> Playlist => _lazyPlaylistFaker.Value;
        public Faker<MusicTrack> MusicTrack => _lazyMusicTrackFaker.Value;
        public Faker<Performer> Performer => _lazyPerformerFaker.Value;
        public Faker<RecordCompany> RecordCompany => _lazyRecordCompanyFaker.Value;
    }
}