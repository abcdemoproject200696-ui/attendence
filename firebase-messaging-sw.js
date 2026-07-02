// Firebase Cloud Messaging service worker — shows web push notifications when the
// browser tab is in the background / minimised. Registered automatically by the
// firebase_messaging web plugin (must live at the web root).
importScripts('https://www.gstatic.com/firebasejs/10.12.2/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.12.2/firebase-messaging-compat.js');

firebase.initializeApp({
  apiKey: "AIzaSyBBOtLkKm8BHdN0OVDBIzTa3GXe6B3_zic",
  authDomain: "attendence-demo-b46c9.firebaseapp.com",
  projectId: "attendence-demo-b46c9",
  storageBucket: "attendence-demo-b46c9.firebasestorage.app",
  messagingSenderId: "953412346693",
  appId: "1:953412346693:web:6acf0ae5b68dc2bfe8542c"
});

// Handles background messages; the notification payload is drawn by the browser.
firebase.messaging();
