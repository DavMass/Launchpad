﻿//
//  MainForm.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2016 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Resources;
using System.Windows.Forms;
using Launchpad.Launcher.Handlers;
using Launchpad.Launcher.Utility.Enums;
using Launchpad.Launcher.Handlers.Protocols;

namespace Launchpad.Launcher.UI
{
	internal partial class MainForm : Form
	{
		/// <summary>
		/// The catalog responsible for handling all of the localizable strings
		/// </summary>
		ResourceManager LocalizationCatalog = new ResourceManager("Launchpad.Launcher.Resources.Strings", typeof(MainForm).Assembly);

		/// <summary>
		/// The checks handler reference.
		/// </summary>
		ChecksHandler Checks = new ChecksHandler();

		/// <summary>
		/// The config handler reference.
		/// </summary>
		ConfigHandler Config = ConfigHandler._instance;

		/// <summary>
		/// The launcher handler. Allows updating the launcher and loading the changelog
		/// </summary>
		LauncherHandler Launcher = new LauncherHandler();

		/// <summary>
		/// The game handler. Allows updating, installing and repairing the game.
		/// </summary>
		GameHandler Game = new GameHandler();

		/// <summary>
		/// The current mode that the launcher is in. Determines what the primary button does when pressed.
		/// </summary>
		ELauncherMode Mode = ELauncherMode.Idle;

		public MainForm()
		{
			InitializeComponent();

			Config.Initialize();

			MessageLabel.Text = LocalizationCatalog.GetString("idleString");
			downloadProgressLabel.Text = String.Empty;

			//set the window text to match the game name
			this.Text = "Launchpad - " + Config.GetGameName();

			//first of all, check if we can connect to the FTP server.
			if (!Checks.CanPatch())
			{
				MessageBox.Show(
					this,
					LocalizationCatalog.GetString("ftpConnectionFailureMessage"),
					LocalizationCatalog.GetString("ftpConnectionFailureString"),
					MessageBoxButtons.OK,
					MessageBoxIcon.Error,
					MessageBoxDefaultButton.Button1);

				MessageLabel.Text = LocalizationCatalog.GetString("ftpConnectionFailureString");
				PrimaryButton.Text = ":(";
				PrimaryButton.Enabled = false;
			}
			else
			{
				//if we can connect, proceed with the rest of our checks.                
				if (ChecksHandler.IsInitialStartup())
				{
					DialogResult shouldInstallHere = MessageBox.Show(
						                                 this,
						                                 String.Format(
							                                 LocalizationCatalog.GetString("initialStartupMessage"), ConfigHandler.GetLocalDir()),
						                                 LocalizationCatalog.GetString("infoTitle"),
						                                 MessageBoxButtons.YesNo,
						                                 MessageBoxIcon.Question,
						                                 MessageBoxDefaultButton.Button1);

					if (shouldInstallHere == DialogResult.Yes)
					{
						//yes, install here
						ConfigHandler.CreateUpdateCookie();
					}
					else
					{
						//no, don't install here
						Environment.Exit(2);
					}
				}
                

				if (Config.GetAllowAnonymousStats())
				{
					StatsHandler.SendUsageStats();
				}
				else
				{
					#if DEBUG

					using (Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico"))
					{
						if (iconStream != null)
						{
							NotifyIcon noUsageStatsNotification = new NotifyIcon();
							noUsageStatsNotification.Icon = new System.Drawing.Icon(iconStream);
							noUsageStatsNotification.Visible = true;

							noUsageStatsNotification.BalloonTipTitle = LocalizationCatalog.GetString("infoTitle");
							noUsageStatsNotification.BalloonTipText = LocalizationCatalog.GetString("usageTitle");

							noUsageStatsNotification.ShowBalloonTip(10000);
						}  
					}
					 
					#endif
				}

				// Load the changelog. Try a direct URL first, and a protocol-specific 
				// implementation after.
				if (Launcher.CanAccessStandardChangelog())
				{
					changelogBrowser.Navigate(Config.GetChangelogURL());
				}
				else
				{
					Launcher.ChangelogDownloadFinished += OnChangelogDownloadFinished;
					Launcher.LoadFallbackChangelog();
				}
                
				//Does the launcher need an update?
				if (!Checks.IsLauncherOutdated())
				{					
					if (!Checks.IsGameInstalled())
					{
						SetLauncherMode(ELauncherMode.Install, false);
					}
					else
					{
						if (Checks.IsGameOutdated())
						{
							SetLauncherMode(ELauncherMode.Update, false);
						}
						else
						{
							SetLauncherMode(ELauncherMode.Launch, false);
						}
					}
				}
				else
				{
					SetLauncherMode(ELauncherMode.Update, false);
				}
			}      
		}


		/// <summary>
		/// Handles switching between different functionalities depending on what is visible on the button to the user, such as
		/// * Installing
		/// * Updating
		/// * Repairing
		/// * Launching
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Empty arguments.</param>
		private void mainButton_Click(object sender, EventArgs e)
		{
			switch (Mode)
			{
				case ELauncherMode.Repair:
					{
						//bind events for UI updating					
						Game.ProgressChanged += OnModuleInstallationProgressChanged;
						Game.GameDownloadFinished += OnGameDownloadFinished;
						Game.GameDownloadFailed += OnGameDownloadFailed;

						if (Checks.IsPlatformAvailable(Config.GetSystemTarget()))
						{
							//repair the game asynchronously
							Game.VerifyGame();
							SetLauncherMode(ELauncherMode.Repair, true);
						}
						else
						{
							//whoops, the server doesn't provide the game for the platform we requested (usually the on we're running on)
							//alert the user and revert back to the default install mode
							Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
							if (iconStream != null)
							{
								NotifyIcon platformNotProvidedNotification = new NotifyIcon();
								platformNotProvidedNotification.Icon = new System.Drawing.Icon(iconStream);
								platformNotProvidedNotification.Visible = true;

								platformNotProvidedNotification.BalloonTipTitle = LocalizationCatalog.GetString("noPlatformTitle");
								platformNotProvidedNotification.BalloonTipText = LocalizationCatalog.GetString("noPlatformMessage");

								platformNotProvidedNotification.ShowBalloonTip(10000);
							}

							SetLauncherMode(ELauncherMode.Install, false);
						}
						break;
					}
				case ELauncherMode.Install:
					{
						//bind events for UI updating                        
						Game.ProgressChanged += OnModuleInstallationProgressChanged;
						Game.GameDownloadFinished += OnGameDownloadFinished;
						Game.GameDownloadFailed += OnGameDownloadFailed;
                                                                       
						if (Checks.IsPlatformAvailable(Config.GetSystemTarget()))
						{
							//install the game asynchronously
							MessageLabel.Text = LocalizationCatalog.GetString("installingLabel");
							SetLauncherMode(ELauncherMode.Install, true);
							Game.InstallGame();                             
						}
						else
						{
							//whoops, the server doesn't provide the game for the platform we requested (usually the on we're running on)
							//alert the user and revert back to the default install mode
							Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
							if (iconStream != null)
							{
								NotifyIcon platformNotProvidedNotification = new NotifyIcon();
								platformNotProvidedNotification.Icon = new System.Drawing.Icon(iconStream);
								platformNotProvidedNotification.Visible = true;

								platformNotProvidedNotification.BalloonTipTitle = LocalizationCatalog.GetString("noPlatformTitle");
								platformNotProvidedNotification.BalloonTipText = LocalizationCatalog.GetString("noPlatformMessage");

								platformNotProvidedNotification.ShowBalloonTip(10000);
							}

							MessageLabel.Text = LocalizationCatalog.GetString("noPlatformMessage");

							SetLauncherMode(ELauncherMode.Install, false);
						}   

						break;
					}
				case ELauncherMode.Update:
					{
						//bind events for UI updating                        
						Game.ProgressChanged += OnModuleInstallationProgressChanged;
						Game.GameDownloadFinished += OnGameDownloadFinished;
						Game.GameDownloadFailed += OnGameDownloadFailed;

						if (Checks.IsLauncherOutdated())
						{
							//update the launcher synchronously.
							SetLauncherMode(ELauncherMode.Update, true);
							Launcher.UpdateLauncher();                            
						}
						else
						{
							if (Checks.IsPlatformAvailable(Config.GetSystemTarget()))
							{
								//update the game asynchronously                                
								SetLauncherMode(ELauncherMode.Update, true);                                
								Game.UpdateGame();                                
							}
							else
							{
								//whoops, the server doesn't provide the game for the platform we requested (usually the on we're running on)
								//alert the user and revert back to the default install mode
								Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
								if (iconStream != null)
								{
									NotifyIcon platformNotProvidedNotification = new NotifyIcon();
									platformNotProvidedNotification.Icon = new System.Drawing.Icon(iconStream);
									platformNotProvidedNotification.Visible = true;

									platformNotProvidedNotification.BalloonTipTitle = LocalizationCatalog.GetString("noPlatformTitle");
									platformNotProvidedNotification.BalloonTipText = LocalizationCatalog.GetString("noPlatformMessage");

									platformNotProvidedNotification.ShowBalloonTip(10000);
								}

								SetLauncherMode(ELauncherMode.Install, false);
							}
						}
                        
						break;
					}
				case ELauncherMode.Launch:
					{
						Game.GameLaunchFailed += OnGameLaunchFailed;
						Game.GameExited += OnGameExited;

						SetLauncherMode(ELauncherMode.Launch, true);
						Game.LaunchGame();

						break;
					}
				default:
					{
						Console.WriteLine("No functionality for this mode.");
						break;
					}
			}
		}

		/// <summary>
		/// Sets the launcher mode and updates UI elements to match
		/// </summary>
		/// <param name="newMode">New mode.</param>
		/// <param name="bInProgress">If set to <c>true</c>, the selected mode is in progress.</param>
		private void SetLauncherMode(ELauncherMode newMode, bool bInProgress)
		{
			//set the global launcher mode
			Mode = newMode;

			//set the UI elements to match
			switch (newMode)
			{
				case ELauncherMode.Install:
					{
						if (bInProgress)
						{
							PrimaryButton.Enabled = false;
							PrimaryButton.Text = LocalizationCatalog.GetString("installingLabel");
						}
						else
						{
							PrimaryButton.Enabled = true;
							PrimaryButton.Text = LocalizationCatalog.GetString("installLabel");
						}
						break;
					}
				case ELauncherMode.Update:
					{
						if (bInProgress)
						{
							PrimaryButton.Enabled = false;
							PrimaryButton.Text = LocalizationCatalog.GetString("updatingLabel");
						}
						else
						{
							PrimaryButton.Enabled = true;
							PrimaryButton.Text = LocalizationCatalog.GetString("updateLabel");
						}
						break;
					}
				case ELauncherMode.Repair:
					{
						if (bInProgress)
						{
							PrimaryButton.Enabled = false;
							PrimaryButton.Text = LocalizationCatalog.GetString("repairingLabel");
						}
						else
						{
							PrimaryButton.Enabled = true;
							PrimaryButton.Text = LocalizationCatalog.GetString("repairLabel");
						}
						break;
					}
				case ELauncherMode.Launch:
					{
						if (bInProgress)
						{
							PrimaryButton.Enabled = false;
							PrimaryButton.Text = LocalizationCatalog.GetString("launchingLabel");
						}
						else
						{
							PrimaryButton.Enabled = true;
							PrimaryButton.Text = LocalizationCatalog.GetString("launchLabel");
						}
						break;
					}
				default:
					{
						throw new ArgumentOutOfRangeException("newMode", "Invalid mode was passed to SetLauncherMode");
					}
			}
		}

		/// <summary>
		/// Updates the web browser with the asynchronously loaded changelog from the server.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">The arguments containing the HTML from the server.</param>
		private void OnChangelogDownloadFinished(object sender, ChangelogDownloadFinishedEventArgs e)
		{
			changelogBrowser.DocumentText = e.HTML;
			changelogBrowser.Url = new Uri(e.URL);
			changelogBrowser.Refresh();         
		}

		/// <summary>
		/// Warns the user when the game fails to launch, and offers to attempt a repair.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Empty event args.</param>
		private void OnGameLaunchFailed(object sender, EventArgs e)
		{
			this.Invoke((MethodInvoker)delegate
				{
					Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
					if (iconStream != null)
					{
						NotifyIcon launchFailedNotification = new NotifyIcon();
						launchFailedNotification.Icon = new System.Drawing.Icon(iconStream);
						launchFailedNotification.Visible = true;

						launchFailedNotification.BalloonTipTitle = LocalizationCatalog.GetString("errorTitle");
						launchFailedNotification.BalloonTipText = LocalizationCatalog.GetString("launchFailMessage");

						launchFailedNotification.ShowBalloonTip(10000);
					}

					SetLauncherMode(ELauncherMode.Repair, false);
				});
		}

		/// <summary>
		/// Provides alternatives when the game fails to download, either through an update or through an installation.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the type of failure that occurred.</param>
		private void OnGameDownloadFailed(object sender, EventArgs e)
		{
			this.Invoke((MethodInvoker)delegate
				{
					switch (Mode)
					{
						case ELauncherMode.Install:
							{
								SetLauncherMode(Mode, false);
								break;
							}
						case ELauncherMode.Update:
							{
								SetLauncherMode(Mode, false);
								break;
							}
						case ELauncherMode.Repair:
							{
								SetLauncherMode(Mode, false);
								break;
							}
						default:
							{
								SetLauncherMode(ELauncherMode.Repair, false);
								break;
							}
					}
				});                                   
		}

		/// <summary>
		/// Updates the progress bar and progress label during installations, repairs and updates.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the progress values and current filename.</param>
		private void OnModuleInstallationProgressChanged(object sender, ModuleProgressChangedArgs e)
		{
			this.Invoke((MethodInvoker)delegate
				{
					MessageLabel.Text = e.IndicatorLabelMessage;
					downloadProgressLabel.Text = e.ProgressBarMessage;

					mainProgressBar.Minimum = 0;
					mainProgressBar.Maximum = 10000;

					double fraction = e.ProgressFraction * 10000;
					mainProgressBar.Value = (int)fraction;
					mainProgressBar.Update();  
               
				});                      
		}

		/// <summary>
		/// Allows the user to launch or repair the game once installation finishes.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the result of the download.</param>
		protected void OnGameDownloadFinished(object sender, EventArgs e)
		{
			this.Invoke((MethodInvoker)delegate
				{
					if (e == null) //there was an error
					{
						MessageLabel.Text = LocalizationCatalog.GetString("gameDownloadFailMessage");

						Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
						if (iconStream != null)
						{
							NotifyIcon launchFailedNotification = new NotifyIcon();
							launchFailedNotification.Icon = new System.Drawing.Icon(iconStream);
							launchFailedNotification.Visible = true;

							launchFailedNotification.BalloonTipTitle = LocalizationCatalog.GetString("errorTitle");
							launchFailedNotification.BalloonTipText = LocalizationCatalog.GetString("gameDownloadFailMessage");

							launchFailedNotification.ShowBalloonTip(10000);
						}

						SetLauncherMode(ELauncherMode.Repair, false);
					}
					else //the game has finished downloading, and we should be OK to launch
					{
						MessageLabel.Text = LocalizationCatalog.GetString("idleString");
						downloadProgressLabel.Text = "";

						Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
						if (iconStream != null)
						{
							NotifyIcon launchFailedNotification = new NotifyIcon();
							launchFailedNotification.Icon = new System.Drawing.Icon(iconStream);
							launchFailedNotification.Visible = true;

							launchFailedNotification.BalloonTipTitle = LocalizationCatalog.GetString("infoTitle");
							launchFailedNotification.BalloonTipText = LocalizationCatalog.GetString("gameDownloadFinishedMessage");

							launchFailedNotification.ShowBalloonTip(10000);
						}

						SetLauncherMode(ELauncherMode.Launch, false);
					}             
				});            
		}

		/// <summary>
		/// Alerts the user that a repair action has finished.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Empty arguments.</param>
		private void OnRepairFinished(object sender, EventArgs e)
		{
			this.Invoke((MethodInvoker)delegate
				{
					Stream iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Launchpad.Launcher.Resources.RocketIcon.ico");
					if (iconStream != null)
					{
						NotifyIcon launchFailedNotification = new NotifyIcon();
						launchFailedNotification.Icon = new System.Drawing.Icon(iconStream);
						launchFailedNotification.Visible = true;

						launchFailedNotification.BalloonTipTitle = LocalizationCatalog.GetString("repairFinishTitle");
						launchFailedNotification.BalloonTipText = LocalizationCatalog.GetString("repairFinishMessage");

						launchFailedNotification.ShowBalloonTip(10000);
					}

					downloadProgressLabel.Text = "";

					SetLauncherMode(ELauncherMode.Launch, false);
				});                       
		}

		/// <summary>
		/// Passes the update finished event to a generic handler.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="e">Contains the result of the download.</param>
		private void OnGameUpdateFinished(object sender, EventArgs e)
		{
			OnGameDownloadFinished(sender, e);
		}

		private void aboutLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			LaunchpadAboutBox about = new LaunchpadAboutBox();
			about.ShowDialog();
		}

		private void OnGameExited(object sender, GameExitEventArgs e)
		{
			if (e.ExitCode != 0)
			{
				SetLauncherMode(ELauncherMode.Repair, false);
			}
			else
			{
				SetLauncherMode(ELauncherMode.Launch, false);
			}
		}
	}
}
