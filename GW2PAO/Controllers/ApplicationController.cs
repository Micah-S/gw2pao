﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GW2PAO.API.Services;
using GW2PAO.Controllers.Interfaces;
using GW2PAO.Models;
using GW2PAO.Utility;
using GW2PAO.ViewModels.Interfaces;
using GW2PAO.ViewModels.TrayIcon;
using GW2PAO.ViewModels.ZoneCompletion;
using GW2PAO.Views;
using GW2PAO.Views.EventTracker;
using GW2PAO.Views.ZoneCompletion;
using NLog;

namespace GW2PAO.Controllers
{
    /// <summary>
    /// The primary application controller
    /// This class is responsible for glueing the program together, created the various services, controllers, views, and view models
    /// </summary>
    public class ApplicationController
    {
        /// <summary>
        /// Default logger
        /// </summary>
        private static Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Service responsible for Event information
        /// </summary>
        public EventsService EventsService { get; private set; }

        /// <summary>
        /// Service responsible for Player information
        /// </summary>
        public PlayerService PlayerService { get; private set; }

        /// <summary>
        /// Service responsible for System information
        /// </summary>
        public SystemService SystemService { get; private set; }

        /// <summary>
        /// Service responsible for Zone information
        /// </summary>
        public ZoneService ZoneService { get; private set; }

        /// <summary>
        /// Event tracker controller
        /// </summary>
        public IEventsController EventsController { get; private set; }

        /// <summary>
        /// Zone completion controller
        /// </summary>
        public IZoneCompletionController ZoneCompletionController { get; private set; }

        /// <summary>
        /// Object that displays the current zone's name (used for the Zone Completion Assistant)
        /// </summary>
        public IHasZoneName ZoneName { get; private set; }

        /// <summary>
        /// Event tracker settings
        /// </summary>
        public EventSettings EventSettings { get; private set; }

        /// <summary>
        /// Zone completion settings
        /// </summary>
        public ZoneCompletionSettings ZoneCompletionSettings { get; private set; }

        /// <summary>
        /// Main functionality menu items, including those for the Event Tracker 
        /// and the Zone Completion Assistant
        /// </summary>
        private List<MenuItemViewModel> menuItems = new List<MenuItemViewModel>();

        /// <summary>
        /// The Event Tracker view
        /// </summary>
        private EventTrackerView eventTrackerView;

        /// <summary>
        /// The Zone Completion Assistant view
        /// </summary>
        private ZoneCompletionView zoneCompletionView;

        /// <summary>
        /// Boolean for keeping track of the "Running As Admin" error shown when GW2 is
        /// running as administrator - prevents spamming the error message
        /// </summary>
        private bool runningAsAdminErrorShown = false;

        /// <summary>
        /// Default constructor
        /// </summary>
        public ApplicationController()
        {
            // Create services
            logger.Debug("Creating services");
            this.EventsService = new EventsService();
            this.PlayerService = new PlayerService();
            this.SystemService = new SystemService();
            this.ZoneService = new ZoneService();

            // Create ZoneName view model for the Zone Completion Assistant
            this.ZoneName = new ZoneNameViewModel();

            // Load user settings
            logger.Debug("Loading event tracker user settings");
            this.EventSettings = EventSettings.LoadSettings();
            if (this.EventSettings == null)
                this.EventSettings = new EventSettings();

            logger.Debug("Loading zone completion assistant user settings");
            this.ZoneCompletionSettings = ZoneCompletionSettings.LoadSettings();
            if (this.ZoneCompletionSettings == null)
                this.ZoneCompletionSettings = new ZoneCompletionSettings();

            // Enable autosave on the user settings
            logger.Debug("Enabling autosave of user settings");
            this.EventSettings.EnableAutoSave();
            this.ZoneCompletionSettings.EnableAutoSave();

            // Create the controllers
            logger.Debug("Creating events controller");
            this.EventsController = new EventsController(this.EventsService, this.EventSettings);
            this.EventsController.EventNotificationEvent += EventsController_EventNotificationEvent;
            this.EventsController.Start(); // Get it started for event notifications

            logger.Debug("Creating zone completion assistant controller");
            this.ZoneCompletionController = new ZoneCompletionController(this.ZoneService, this.PlayerService, this.SystemService, this.ZoneName, this.ZoneCompletionSettings);

            // Initialize the menu items
            logger.Debug("Initializing application menu items");
            this.menuItems.Add(new MenuItemViewModel("Open Events Tracker", this.DisplayEventTracker, this.CanDisplayEventTracker));
            this.menuItems.Add(new MenuItemViewModel("Event Notifications", null, true, () => { return this.EventSettings.AreEventNotificationsEnabled; }, (enabled) => this.EventSettings.AreEventNotificationsEnabled = enabled));
            this.menuItems.Add(new MenuItemViewModel("Open Zone Completion Assistant", this.DisplayZoneAssistant, this.CanDisplayZoneAssistant));

            logger.Info("Application controller initialized");
        }

        /// <summary>
        /// Returns the main application menu items
        /// </summary>
        /// <returns>The main application menu items</returns>
        public IEnumerable<MenuItemViewModel> GetMenuItems()
        {
            return this.menuItems;
        }

        /// <summary>
        /// Displays the Event Tracker window, or, if already displayed, sets
        /// focus to the window
        /// </summary>
        private void DisplayEventTracker()
        {
            if (this.eventTrackerView == null || !this.eventTrackerView.IsVisible)
            {
                this.EventsController.Start();
                this.eventTrackerView = new EventTrackerView(this.EventsController);
                this.eventTrackerView.Show();
            }
            else
            {
                this.eventTrackerView.Focus();
            }
        }

        /// <summary>
        /// Determines if the event tracker can be displayed
        /// </summary>
        /// <returns>Always true</returns>
        private bool CanDisplayEventTracker()
        {
            return true;
        }

        /// <summary>
        /// Displays the Zone Completion Assistant window, or, if already displayed,
        /// sets focus to the window
        /// </summary>
        private void DisplayZoneAssistant()
        {
            if (this.zoneCompletionView == null || !this.zoneCompletionView.IsVisible)
            {
                this.ZoneCompletionController.Start();
                this.zoneCompletionView = new ZoneCompletionView(this.ZoneCompletionController, this.ZoneName);
                this.zoneCompletionView.Show();
            }
            else
            {
                this.zoneCompletionView.Focus();
            }
        }

        /// <summary>
        /// Determines if the zone assistant can be displayed
        /// </summary>
        /// <returns></returns>
        private bool CanDisplayZoneAssistant()
        {
            bool canDisplayZoneAssistant = false;

            try
            {
                canDisplayZoneAssistant = (this.SystemService.IsGw2Running && this.PlayerService.HasValidMapId);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // An exception can happen if GW2 is running as admin
                // If that occurs, display a notification
                if (ex.NativeErrorCode == 5 && !this.runningAsAdminErrorShown)
                {
                    App.TrayIcon.DisplayNotification("Warning", "The Zone Completion Assistant cannot be started because GW2 is running as administrator.", TrayIcon.TrayInfoMessageType.Warning);
                    logger.Warn(ex);
                    this.runningAsAdminErrorShown = true;
                }
            }

            if (canDisplayZoneAssistant)
                this.runningAsAdminErrorShown = false;

            return canDisplayZoneAssistant;
        }

        /// <summary>
        /// Event handler for the event
        /// </summary>
        private void EventsController_EventNotificationEvent(object sender, EventNotificationArgs e)
        {
            if (this.EventSettings.AreEventNotificationsEnabled)
            {
                // TODO: We may need to change how we show these and use an actual window, as it's possible for 2 events to be active at the same time, so we need to be able to show more than 1 event at once
                Threading.BeginInvokeOnUI(() => App.TrayIcon.DisplayCustomNotification(new EventNotificationView(e.EventData)));
            }
        }
    }
}
