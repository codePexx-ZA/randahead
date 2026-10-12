// App settings: API address, demo account hints, account rules and the industry list
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const MIN_PASSWORD_LENGTH = 8;

const CONFIG = {
  API_BASE: "http://localhost:5080/api",
  SHOW_DEMO_ACCOUNTS: true,
};

const INDUSTRIES = [
  "Retail",
  "Wholesale",
  "Manufacturing",
  "Construction",
  "Hospitality and tourism",
  "Transport and logistics",
  "Professional services",
  "Agriculture",
  "Health",
  "Education",
  "Information technology",
  "Personal services",
  "Other",
];
