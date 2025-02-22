using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Fluent.Extensions;
using WalletWasabi.Fluent.Helpers;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Labels;

public partial class SuggestionLabelsViewModel : ViewModelBase
{
	private readonly SourceList<SuggestionLabelViewModel> _sourceLabels;
	private readonly ObservableCollectionExtended<string> _topSuggestions;
	private readonly ObservableCollectionExtended<string> _suggestions;
	private readonly ObservableCollectionExtended<string> _labels;

	[AutoNotify] private bool _isCurrentTextValid;

	public SuggestionLabelsViewModel(KeyManager keyManager, Intent intent, int topSuggestionsCount, IEnumerable<string>? labels = null)
	{
		KeyManager = keyManager;
		Intent = intent;
		_sourceLabels = new SourceList<SuggestionLabelViewModel>();
		_topSuggestions = new ObservableCollectionExtended<string>();
		_suggestions = new ObservableCollectionExtended<string>();
		_labels = new ObservableCollectionExtended<string>(labels ?? new List<string>());

		UpdateLabels();
		CreateSuggestions(topSuggestionsCount);
	}

	public ObservableCollection<string> TopSuggestions => _topSuggestions;

	public ObservableCollection<string> Suggestions => _suggestions;

	public ObservableCollection<string> Labels => _labels;

	public KeyManager KeyManager { get; }
	public Intent Intent { get; }

	public void UpdateLabels()
	{
		var labelPool = new Dictionary<string, int>(); // int: score.

		// Make recent and receive labels count more for the current wallet
		var multiplier = 100;
		foreach (var label in KeyManager.GetReceiveLabels().Reverse().SelectMany(x => x))
		{
			var score = (Intent == Intent.Receive ? 100 : 1) * multiplier;
			if (!labelPool.TryAdd(label, score))
			{
				labelPool[label] += score;
			}

			if (multiplier > 1)
			{
				multiplier--;
			}
		}

		// Receive addresses should be more dominant.
		foreach (var label in WalletHelpers.GetReceiveAddressLabels().SelectMany(x => x))
		{
			var score = Intent == Intent.Receive ? 100 : 1;
			if (!labelPool.TryAdd(label, score))
			{
				labelPool[label] += score;
			}
		}

		// Change addresses shouldn't be much dominant, but should be present.
		foreach (var label in WalletHelpers.GetChangeAddressLabels().SelectMany(x => x))
		{
			var score = 1;
			if (!labelPool.TryAdd(label, score))
			{
				labelPool[label] += score;
			}
		}

		multiplier = 100; // Make recent labels count more.
		foreach (var label in WalletHelpers.GetTransactionLabels().SelectMany(x => x).Reverse())
		{
			var score = (Intent == Intent.Send ? 100 : 1) * multiplier;
			if (!labelPool.TryAdd(label, score))
			{
				labelPool[label] += score;
			}

			if (multiplier > 1)
			{
				multiplier--;
			}
		}

		var unwantedLabelSuggestions = new[]
		{
			"test", // Often people use the string "test" as a label. It obviously cannot be a real label, just a test label.
			"zerolink mixed coin", // Obsolated autogenerated label from old WW1 versions.
			"zerolink change", // Obsolated autogenerated label from old WW1 versions.
			"zerolink dequeued change" // Obsolated autogenerated label from old WW1 versions.
		};

		var labels = labelPool
			.Where(x =>
				!unwantedLabelSuggestions.Any(y => y.Equals(x.Key, StringComparison.OrdinalIgnoreCase))
				&& !x.Key.StartsWith("change of (", StringComparison.OrdinalIgnoreCase)); // An obsolated autogenerated label pattern was from old WW1 versions starting with "change of (".

		var mostUsedLabels = labels
			.GroupBy(x => x.Key)
			.Select(x => new
			{
				Label = x.Key,
				Score = x.Sum(y => y.Value)
			})
			.OrderByDescending(x => x.Score)
			.ToList();

		_sourceLabels.Clear();
		_sourceLabels.AddRange(
			mostUsedLabels.Select(x => new SuggestionLabelViewModel(x.Label, x.Score)));
	}

	private void CreateSuggestions(int topSuggestionsCount)
	{
		var suggestionLabelsFilter = this.WhenAnyValue(x => x.Labels).ToSignal()
			.Merge(Observable.FromEventPattern(Labels, nameof(Labels.CollectionChanged)).ToSignal())
			.Select(_ => SuggestionLabelsFilter());

		_sourceLabels
			.Connect()
			.Filter(suggestionLabelsFilter)
			.Sort(SortExpressionComparer<SuggestionLabelViewModel>.Descending(x => x.Score).ThenByAscending(x => x.Label))
			.Top(topSuggestionsCount)
			.Transform(x => x.Label)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(_topSuggestions)
			.Subscribe();

		_sourceLabels
			.Connect()
			.Filter(suggestionLabelsFilter)
			.Sort(SortExpressionComparer<SuggestionLabelViewModel>.Descending(x => x.Score).ThenByAscending(x => x.Label))
			.Transform(x => x.Label)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(_suggestions)
			.Subscribe();

		Func<SuggestionLabelViewModel, bool> SuggestionLabelsFilter() => suggestionLabel => !_labels.Contains(suggestionLabel.Label);
	}
}
