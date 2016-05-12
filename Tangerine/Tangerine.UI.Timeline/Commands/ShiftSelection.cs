﻿using System;
using System.Collections.Generic;
using System.Linq;
using Lime;
using Tangerine.Core;

namespace Tangerine.UI.Timeline.Commands
{
	public class ShiftSelection : ICommand
	{
		IntVector2 offset;

		public ShiftSelection(IntVector2 offset)
		{
			this.offset = offset;
		}

		public void Do()
		{
			Shift(offset);
		}

		public void Undo()
		{
			Shift(-offset);
		}

		void Shift(IntVector2 offset)
		{
			var s = Timeline.Instance.GridSelection;
			for (int i = 0; i < s.Count; i++) {
				var r = s[i];
				r.A += offset;
				r.B += offset;
				s[i] = r;
			}
		}
	}
}